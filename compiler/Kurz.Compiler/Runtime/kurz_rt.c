/*
 * The runtime a compiled Kurz program links (design record, section 2): the memory forms of strings
 * and lists with the primitives the emitted IR calls, the text of the primitive values, `print`,
 * the one line an exception writes and the exit it ends with (section 6), the handler that turns a
 * stack overflow into that line, and the allocator. It is compiled by the same clang as the emitted
 * IR and linked statically. It calls the C library and the operating system at the boundary only:
 * the raw write call, an exit that runs no handler of the C library, allocating memory, the signal
 * stack and the fault handler of the thread (section 2). It uses no stream of the C library.
 *
 * Every parameter and result of a function the IR calls is a scalar: a string or a list crosses
 * the boundary as its fields, or through a pointer to where the function writes it, because the
 * platforms pass a 16-byte struct by value differently (compiler/Kurz.Compiler/Emit/Runtime.cs
 * declares the same functions on the IR side).
 *
 * Memory is never freed: the reference counting of section 3 is not in this runtime, and every
 * allocation stays until the process ends. A list buffer is shared between the values that hold it
 * and grown by appending while the appending value holds the buffer's last item, so that a list
 * value never changes under a variable that holds an older one (M1, M3).
 */

#ifndef _WIN32
#define _XOPEN_SOURCE 700 /* the signal stack, siginfo_t and SA_ONSTACK are hidden under -std=c11 without it */
#endif

#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <unistd.h>
#include <signal.h>
#include <errno.h>
#endif

typedef struct { const uint8_t *ptr; int64_t len; } kz_str;
typedef struct { int64_t cap; int64_t filled; } kz_buf;      /* the items follow the header */
typedef struct { kz_buf *buf; int64_t count; } kz_list;

/* The runtime's own copy, fill, compare and length: the boundary with the C library that the record
   draws (section 2) lists no string function, so the runtime moves its bytes itself; what the optimizer
   makes of these loops is its own lowering. */
static void kz_copy(void *dst, const void *src, int64_t n)
{
    uint8_t *d = (uint8_t *)dst;
    const uint8_t *s = (const uint8_t *)src;
    for (int64_t i = 0; i < n; i++) {
        d[i] = s[i];
    }
}

static void kz_zero(void *dst, int64_t n)
{
    uint8_t *d = (uint8_t *)dst;
    for (int64_t i = 0; i < n; i++) {
        d[i] = 0;
    }
}

static int kz_same(const uint8_t *a, const uint8_t *b, int64_t n)
{
    for (int64_t i = 0; i < n; i++) {
        if (a[i] != b[i]) {
            return 0;
        }
    }
    return 1;
}

static int64_t kz_cstr_len(const char *s)
{
    int64_t n = 0;
    while (s[n] != 0) {
        n++;
    }
    return n;
}

/* The line of the call that is about to run, stored before every call (section 6). */
int32_t kz_line = 0;

/* ---- the boundary: write and exit ---------------------------------------------------------- */

static void kz_write_all(int fd, const uint8_t *p, int64_t n)
{
    while (n > 0) {
#ifdef _WIN32
        HANDLE h = GetStdHandle(fd == 2 ? STD_ERROR_HANDLE : STD_OUTPUT_HANDLE);
        DWORD took = 0;
        DWORD want = n > 0x7fffffff ? 0x7fffffffu : (DWORD)n;
        if (!WriteFile(h, p, want, &took, NULL)) {
            return;                                  /* a closed stream: nothing more can be said */
        }
#else
        ssize_t took = write(fd, p, (size_t)n);
        if (took < 0) {
            if (errno == EINTR) {
                continue;
            }
            return;
        }
#endif
        p += took;
        n -= took;
    }
}

static void kz_exit_now(int code)
{
#ifdef _WIN32
    TerminateProcess(GetCurrentProcess(), (UINT)code);
#else
    _exit(code);
#endif
    for (;;) { }
}

/* ---- the allocator -------------------------------------------------------------------------- */

#define KZ_CHUNK (1 << 20)

static uint8_t *kz_chunk_at;
static int64_t kz_chunk_left;

static void kz_allocation_failed(void)
{
    static const char text[] = "error: allocation failed\n";
    kz_write_all(2, (const uint8_t *)text, (int64_t)sizeof text - 1);
    kz_exit_now(1);
}

void *kz_alloc(int64_t size)
{
    if (size < 0) {
        kz_allocation_failed();
    }
    size = (size + 15) & ~(int64_t)15;
    if (size > KZ_CHUNK / 4) {
        void *big = malloc((size_t)size);
        if (big == NULL) {
            kz_allocation_failed();
        }
        return big;
    }
    if (size > kz_chunk_left) {
        kz_chunk_at = (uint8_t *)malloc(KZ_CHUNK);
        if (kz_chunk_at == NULL) {
            kz_allocation_failed();
        }
        kz_chunk_left = KZ_CHUNK;
    }
    void *p = kz_chunk_at;
    kz_chunk_at += size;
    kz_chunk_left -= size;
    return p;
}

/* ---- exceptions ------------------------------------------------------------------------------ */

static int64_t kz_digits(uint64_t value, uint8_t *out)
{
    uint8_t tmp[24];
    int64_t n = 0;
    do {
        tmp[n++] = (uint8_t)('0' + value % 10);
        value /= 10;
    } while (value != 0);
    for (int64_t i = 0; i < n; i++) {
        out[i] = tmp[n - 1 - i];
    }
    return n;
}

/* "error: <id> at line <n>" and, where a text is given, ": <text>", then one line feed, on standard error. */
static void kz_error_line(const char *id, int32_t line, const uint8_t *text, int64_t text_len)
{
    static uint8_t head[128];                        /* static: the stack-overflow handler has little stack */
    int64_t n = 0;
    static const char lead[] = "error: ";
    kz_copy(head + n, lead, (int64_t)sizeof lead - 1);
    n += (int64_t)sizeof lead - 1;
    int64_t idlen = kz_cstr_len(id);
    if (idlen > 64) {
        idlen = 64;
    }
    kz_copy(head + n, id, idlen);
    n += idlen;
    static const char at[] = " at line ";
    kz_copy(head + n, at, (int64_t)sizeof at - 1);
    n += (int64_t)sizeof at - 1;
    if (line < 0) {
        head[n++] = '-';
        n += kz_digits((uint64_t)(-(int64_t)line), head + n);
    } else {
        n += kz_digits((uint64_t)line, head + n);
    }
    if (text != NULL) {
        head[n++] = ':';
        head[n++] = ' ';
        kz_write_all(2, head, n);
        kz_write_all(2, text, text_len);
        static const uint8_t lf = '\n';
        kz_write_all(2, &lf, 1);
        return;
    }
    head[n++] = '\n';
    kz_write_all(2, head, n);
}

/* An exception of the run-time table (reference, chapter 12) ends the process with the exit code 1 (O6). */
void kz_fail(const char *id, int32_t line)
{
    kz_error_line(id, line, NULL, 0);
    kz_exit_now(1);
}

/* A `throw`: the id `thrown`, the line of the throw, and the text of the thrown value after it (section 6). */
void kz_throw(const uint8_t *text, int64_t text_len, int32_t line)
{
    kz_error_line("thrown", line, text, text_len);
    kz_exit_now(1);
}

/* ---- the stack overflow (O11) -------------------------------------------------------------- */

#ifdef _WIN32
static LONG WINAPI kz_on_fault(EXCEPTION_POINTERS *info)
{
    if (info->ExceptionRecord->ExceptionCode == EXCEPTION_STACK_OVERFLOW) {
        kz_fail("stack-overflow", kz_line);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
#else
static uint8_t kz_signal_stack[64 * 1024];

static void kz_on_fault(int signal, siginfo_t *info, void *context)
{
    (void)signal;
    (void)info;
    (void)context;
    kz_fail("stack-overflow", kz_line);
}
#endif

/* Called first by the emitted `main`: the handler that turns the fault of a stack overflow into the exception's line. */
void kz_init(void)
{
#ifdef _WIN32
    ULONG guarantee = 64 * 1024;
    SetThreadStackGuarantee(&guarantee);
    AddVectoredExceptionHandler(1, kz_on_fault);
#else
    stack_t alt;
    alt.ss_sp = kz_signal_stack;
    alt.ss_size = sizeof kz_signal_stack;
    alt.ss_flags = 0;
    sigaltstack(&alt, NULL);
    struct sigaction action;
    kz_zero(&action, (int64_t)sizeof action);
    action.sa_sigaction = kz_on_fault;
    action.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&action.sa_mask);
    sigaction(SIGSEGV, &action, NULL);
    sigaction(SIGBUS, &action, NULL);
#endif
}

/* ---- print and the text of the primitive values (A1, A2, A8) ------------------------------- */

/* The whole line, the line feed included, goes to the write call in one piece (section 6). */
void kz_print(const uint8_t *text, int64_t len)
{
    uint8_t small[512];
    uint8_t *line = len + 1 <= (int64_t)sizeof small ? small : (uint8_t *)kz_alloc(len + 1);
    kz_copy(line, text, len);
    line[len] = '\n';
    kz_write_all(1, line, len + 1);
}

void kz_text_i64(int64_t value, kz_str *out)
{
    uint8_t *digits = (uint8_t *)kz_alloc(24);
    int64_t n = 0;
    uint64_t magnitude;
    if (value < 0) {
        digits[n++] = '-';
        magnitude = (uint64_t)0 - (uint64_t)value;
    } else {
        magnitude = (uint64_t)value;
    }
    n += kz_digits(magnitude, digits + n);
    out->ptr = digits;
    out->len = n;
}

void kz_text_u64(uint64_t value, kz_str *out)
{
    uint8_t *digits = (uint8_t *)kz_alloc(24);
    out->ptr = digits;
    out->len = kz_digits(value, digits);
}

void kz_text_bool(int8_t value, kz_str *out)
{
    static const uint8_t yes[] = "true";
    static const uint8_t no[] = "false";
    out->ptr = value ? yes : no;
    out->len = value ? 4 : 5;
}

static int64_t kz_utf8(uint32_t cp, uint8_t *out)
{
    if (cp < 0x80) {
        out[0] = (uint8_t)cp;
        return 1;
    }
    if (cp < 0x800) {
        out[0] = (uint8_t)(0xC0 | (cp >> 6));
        out[1] = (uint8_t)(0x80 | (cp & 0x3F));
        return 2;
    }
    if (cp < 0x10000) {
        out[0] = (uint8_t)(0xE0 | (cp >> 12));
        out[1] = (uint8_t)(0x80 | ((cp >> 6) & 0x3F));
        out[2] = (uint8_t)(0x80 | (cp & 0x3F));
        return 3;
    }
    out[0] = (uint8_t)(0xF0 | (cp >> 18));
    out[1] = (uint8_t)(0x80 | ((cp >> 12) & 0x3F));
    out[2] = (uint8_t)(0x80 | ((cp >> 6) & 0x3F));
    out[3] = (uint8_t)(0x80 | (cp & 0x3F));
    return 4;
}

/* The text of a `char` is its character (A8), as UTF-8. */
void kz_text_char(int32_t code_point, kz_str *out)
{
    uint8_t *bytes = (uint8_t *)kz_alloc(4);
    out->ptr = bytes;
    out->len = kz_utf8((uint32_t)code_point, bytes);
}

/* ---- strings (T15) --------------------------------------------------------------------------- */

/* ---- numbers beyond the integers: the texts of A11, A12 and A14, the floating-point remainder,
   `decimal` as System.Decimal computes it, and the conversions of T26 ------------------------- */

/*
 * A small unsigned integer of many words, for the digits of a floating-point number and the exact
 * arithmetic of `decimal`. 48 words hold 1536 bits: the scaled margins of the smallest double need
 * about 1140, and a decimal product scaled by a power of ten about 290.
 */
#define KZ_BIG_WORDS 48
typedef struct { int32_t n; uint32_t w[KZ_BIG_WORDS]; } kz_big;

static void kz_big_norm(kz_big *b)
{
    while (b->n > 0 && b->w[b->n - 1] == 0) {
        b->n--;
    }
}

static void kz_big_copy(kz_big *dst, const kz_big *src)
{
    dst->n = src->n;
    for (int32_t i = 0; i < src->n; i++) {
        dst->w[i] = src->w[i];
    }
}

static void kz_big_set(kz_big *b, uint64_t v)
{
    b->w[0] = (uint32_t)v;
    b->w[1] = (uint32_t)(v >> 32);
    b->n = 2;
    kz_big_norm(b);
}

static void kz_big_set96(kz_big *b, uint64_t low64, uint32_t high)
{
    b->w[0] = (uint32_t)low64;
    b->w[1] = (uint32_t)(low64 >> 32);
    b->w[2] = high;
    b->n = 3;
    kz_big_norm(b);
}

static int kz_big_bits(const kz_big *a)
{
    if (a->n == 0) {
        return 0;
    }
    uint32_t top = a->w[a->n - 1];
    int bits = 0;
    while (top != 0) {
        bits++;
        top >>= 1;
    }
    return (a->n - 1) * 32 + bits;
}

static int kz_big_bit(const kz_big *a, int i)
{
    int w = i / 32;
    return w < a->n ? (int)((a->w[w] >> (i % 32)) & 1) : 0;
}

static void kz_big_mul_small(kz_big *b, uint32_t m)
{
    uint64_t carry = 0;
    for (int i = 0; i < b->n; i++) {
        uint64_t p = (uint64_t)b->w[i] * m + carry;
        b->w[i] = (uint32_t)p;
        carry = p >> 32;
    }
    if (carry != 0) {
        b->w[b->n++] = (uint32_t)carry;
    }
}

static void kz_big_mul_pow10(kz_big *b, int k)
{
    static const uint32_t small[9] = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000 };
    while (k >= 9) {
        kz_big_mul_small(b, 1000000000u);
        k -= 9;
    }
    if (k > 0) {
        kz_big_mul_small(b, small[k]);
    }
}

static void kz_big_shl(kz_big *b, int bits)
{
    if (b->n == 0 || bits == 0) {
        return;
    }
    int words = bits / 32;
    bits %= 32;
    if (bits != 0) {
        uint32_t carry = 0;
        for (int i = 0; i < b->n; i++) {
            uint32_t v = b->w[i];
            b->w[i] = (v << bits) | carry;
            carry = v >> (32 - bits);
        }
        if (carry != 0) {
            b->w[b->n++] = carry;
        }
    }
    if (words != 0) {
        for (int i = b->n - 1; i >= 0; i--) {
            b->w[i + words] = b->w[i];
        }
        for (int i = 0; i < words; i++) {
            b->w[i] = 0;
        }
        b->n += words;
    }
}

static int kz_big_cmp(const kz_big *a, const kz_big *b)
{
    if (a->n != b->n) {
        return a->n < b->n ? -1 : 1;
    }
    for (int i = a->n - 1; i >= 0; i--) {
        if (a->w[i] != b->w[i]) {
            return a->w[i] < b->w[i] ? -1 : 1;
        }
    }
    return 0;
}

static void kz_big_add(kz_big *a, const kz_big *b)
{
    int n = a->n > b->n ? a->n : b->n;
    uint64_t carry = 0;
    for (int i = 0; i < n; i++) {
        uint64_t s = carry + (i < a->n ? a->w[i] : 0) + (i < b->n ? b->w[i] : 0);
        a->w[i] = (uint32_t)s;
        carry = s >> 32;
    }
    a->n = n;
    if (carry != 0) {
        a->w[a->n++] = (uint32_t)carry;
    }
}

/* a -= b, for a >= b */
static void kz_big_sub(kz_big *a, const kz_big *b)
{
    int64_t borrow = 0;
    for (int i = 0; i < a->n; i++) {
        int64_t d = (int64_t)a->w[i] - (i < b->n ? b->w[i] : 0) - borrow;
        borrow = d < 0;
        a->w[i] = (uint32_t)(d + (borrow << 32));
    }
    kz_big_norm(a);
}

static uint32_t kz_big_divmod_small(kz_big *a, uint32_t d)
{
    uint64_t rem = 0;
    for (int i = a->n - 1; i >= 0; i--) {
        uint64_t cur = (rem << 32) | a->w[i];
        a->w[i] = (uint32_t)(cur / d);
        rem = cur % d;
    }
    kz_big_norm(a);
    return (uint32_t)rem;
}

/* r = a * b; r is neither a nor b */
static void kz_big_mul(kz_big *r, const kz_big *a, const kz_big *b)
{
    for (int i = 0; i < a->n + b->n; i++) {
        r->w[i] = 0;
    }
    for (int i = 0; i < a->n; i++) {
        uint64_t carry = 0;
        for (int j = 0; j < b->n; j++) {
            uint64_t p = (uint64_t)a->w[i] * b->w[j] + r->w[i + j] + carry;
            r->w[i + j] = (uint32_t)p;
            carry = p >> 32;
        }
        r->w[i + b->n] = (uint32_t)carry;
    }
    r->n = a->n + b->n;
    kz_big_norm(r);
}

/* q = a / b and r = a % b, a bit at a time; b is not zero */
static void kz_big_divmod(kz_big *q, kz_big *r, const kz_big *a, const kz_big *b)
{
    for (int i = 0; i < KZ_BIG_WORDS; i++) {
        q->w[i] = 0;
    }
    q->n = 0;
    r->n = 0;
    int bits = kz_big_bits(a);
    for (int i = bits - 1; i >= 0; i--) {
        kz_big_shl(r, 1);
        if (kz_big_bit(a, i)) {
            if (r->n == 0) {
                r->n = 1;
                r->w[0] = 1;
            } else {
                r->w[0] |= 1;
            }
        }
        if (kz_big_cmp(r, b) >= 0) {
            kz_big_sub(r, b);
            q->w[i / 32] |= 1u << (i % 32);
        }
    }
    q->n = (bits + 31) / 32;
    kz_big_norm(q);
}

/* ---- the digits of a floating-point number (A4, A11, A14) ---------------------------------- */

/*
 * The shortest digits that read back as the same number, found exactly with the margins of the
 * number's neighbours (the free-format algorithm of Steele and White, as Burger and Dybvig state
 * it, with the ending that System.Double's text uses): the value is f * 2^e with f carrying its
 * hidden bit; the digits are d1..dn with the value 0.d1..dn * 10^k. A margin is reached, not only
 * crossed, when f is even, because such a boundary reads back to the even neighbour; a last digit
 * exactly between two candidates rounds to the even digit.
 */
static int kz_shortest_digits(uint64_t f, int e, int mantissa_bits, int at_min_exponent, uint8_t *digits, int *k_out)
{
    kz_big r, s, mp, mm;
    int even = (f & 1) == 0;
    int boundary = f == ((uint64_t)1 << (mantissa_bits - 1)) && !at_min_exponent; /* the gap below is half the gap above */
    kz_big_set(&r, f);
    kz_big_set(&s, 1);
    kz_big_set(&mp, 1);
    kz_big_set(&mm, 1);
    if (e >= 0) {
        if (!boundary) {
            kz_big_shl(&r, e + 1);
            kz_big_shl(&s, 1);
            kz_big_shl(&mp, e);
            kz_big_shl(&mm, e);
        } else {
            kz_big_shl(&r, e + 2);
            kz_big_shl(&s, 2);
            kz_big_shl(&mp, e + 1);
            kz_big_shl(&mm, e);
        }
    } else if (!boundary) {
        kz_big_shl(&r, 1);
        kz_big_shl(&s, 1 - e);
    } else {
        kz_big_shl(&r, 2);
        kz_big_shl(&s, 2 - e);
        kz_big_shl(&mp, 1);
    }

    /* an estimate of ceil(log10(value)) that is never too high; what it lacks the loop below adds */
    int bitlen = 0;
    for (uint64_t t = f; t != 0; t >>= 1) {
        bitlen++;
    }
    double x = (double)(bitlen - 1 + e) * 0.30102999566398114 - 1e-9;
    int k = (int)x;
    if ((double)k > x) {
        k--;
    }
    if (k >= 0) {
        kz_big_mul_pow10(&s, k);
    } else {
        kz_big_mul_pow10(&r, -k);
        kz_big_mul_pow10(&mp, -k);
        kz_big_mul_pow10(&mm, -k);
    }
    for (;;) {
        kz_big t;
        kz_big_copy(&t, &r);
        kz_big_add(&t, &mp);
        int c = kz_big_cmp(&t, &s);
        if (c > 0 || (c == 0 && even)) {
            k++;
            kz_big_mul_small(&s, 10);
        } else {
            break;
        }
    }

    int n = 0;
    for (;;) {
        kz_big_mul_small(&r, 10);
        kz_big_mul_small(&mp, 10);
        kz_big_mul_small(&mm, 10);
        int d = 0;
        while (kz_big_cmp(&r, &s) >= 0) {
            kz_big_sub(&r, &s);
            d++;
        }
        int cl = kz_big_cmp(&r, &mm);
        int low = cl < 0 || (cl == 0 && even);
        kz_big t;
        kz_big_copy(&t, &r);
        kz_big_add(&t, &mp);
        int ch = kz_big_cmp(&t, &s);
        int high = ch > 0 || (ch == 0 && even);
        if (!low && !high) {
            digits[n++] = (uint8_t)d;
            continue;
        }
        int round_up;
        if (low && !high) {
            round_up = 0;
        } else if (high && !low) {
            round_up = 1;
        } else {
            kz_big twice;
            kz_big_copy(&twice, &r);
            kz_big_shl(&twice, 1);
            int c2 = kz_big_cmp(&twice, &s);
            round_up = c2 > 0 || (c2 == 0 && (d & 1) != 0);
        }
        if (round_up) {
            d++;
            if (d == 10) {
                int i = n;
                while (i > 0 && digits[i - 1] == 9) {
                    i--;
                }
                if (i == 0) {
                    digits[0] = 1;
                    *k_out = k + 1;
                    return 1;
                }
                digits[i - 1]++;
                *k_out = k;
                return i;
            }
        }
        digits[n++] = (uint8_t)d;
        *k_out = k;
        return n;
    }
}

/*
 * The text of the digits as A14 reads System.Double's: the exponent form `d.dddE+xx` when the
 * number is 1E+15 or more or below 1E-04 (the point's place k above `fixed_max_scale`, 15, or
 * below -3), else the digits with the point placed; the exponent has its sign and two digits at
 * least.
 */
static int64_t kz_real_digits_text(int negative, const uint8_t *digits, int n, int k, int fixed_max_scale, uint8_t *out)
{
    int64_t len = 0;
    if (negative) {
        out[len++] = '-';
    }
    if (k > fixed_max_scale || k < -3) {
        out[len++] = (uint8_t)('0' + digits[0]);
        if (n > 1) {
            out[len++] = '.';
            for (int i = 1; i < n; i++) {
                out[len++] = (uint8_t)('0' + digits[i]);
            }
        }
        out[len++] = 'E';
        int exponent = k - 1;
        out[len++] = exponent < 0 ? '-' : '+';
        if (exponent < 0) {
            exponent = -exponent;
        }
        uint8_t tmp[8];
        int64_t m = kz_digits((uint64_t)exponent, tmp);
        if (m < 2) {
            out[len++] = '0';
        }
        kz_copy(out + len, tmp, m);
        len += m;
        return len;
    }
    if (k <= 0) {
        out[len++] = '0';
        out[len++] = '.';
        for (int i = 0; i < -k; i++) {
            out[len++] = '0';
        }
        for (int i = 0; i < n; i++) {
            out[len++] = (uint8_t)('0' + digits[i]);
        }
        return len;
    }
    for (int i = 0; i < k; i++) {
        out[len++] = i < n ? (uint8_t)('0' + digits[i]) : (uint8_t)'0';
    }
    if (n > k) {
        out[len++] = '.';
        for (int i = k; i < n; i++) {
            out[len++] = (uint8_t)('0' + digits[i]);
        }
    }
    return len;
}

#define KZ_FIXED_MAX_SCALE 15 /* A14: 1E+15 and above take the exponent form */

static uint64_t kz_f64_bits(double v)
{
    uint64_t bits;
    kz_copy(&bits, &v, 8);
    return bits;
}

static double kz_f64_from_bits(uint64_t bits)
{
    double v;
    kz_copy(&v, &bits, 8);
    return v;
}

static uint32_t kz_f32_bits(float v)
{
    uint32_t bits;
    kz_copy(&bits, &v, 4);
    return bits;
}

/* The text of a double or a float from its bits: `mantissa_bits` 53 or 24, `exponent_bits` 11 or 8. */
static int64_t kz_real_text(uint64_t bits, int mantissa_bits, int exponent_bits, int fixed_max_scale, uint8_t *out)
{
    int negative = (int)(bits >> (mantissa_bits - 1 + exponent_bits));
    uint64_t fraction = bits & (((uint64_t)1 << (mantissa_bits - 1)) - 1);
    int biased = (int)((bits >> (mantissa_bits - 1)) & (((uint64_t)1 << exponent_bits) - 1));
    int max_biased = (1 << exponent_bits) - 1;
    if (biased == max_biased) {
        static const uint8_t nan[] = "NaN";
        static const uint8_t inf[] = "Infinity";
        if (fraction != 0) {
            kz_copy(out, nan, 3);
            return 3;
        }
        int64_t len = 0;
        if (negative) {
            out[len++] = '-';
        }
        kz_copy(out + len, inf, 8);
        return len + 8;
    }
    if (biased == 0 && fraction == 0) {
        int64_t len = 0;
        if (negative) {
            out[len++] = '-';
        }
        out[len++] = '0';
        return len;
    }
    int bias = (1 << (exponent_bits - 1)) - 1 + (mantissa_bits - 1);
    uint64_t f;
    int e;
    int at_min = 0;
    if (biased == 0) {
        f = fraction;
        e = 1 - bias;
        at_min = 1;
    } else {
        f = fraction | ((uint64_t)1 << (mantissa_bits - 1));
        e = biased - bias;
        at_min = biased == 1;
    }
    uint8_t digits[24];
    int k;
    int n = kz_shortest_digits(f, e, mantissa_bits, at_min, digits, &k);
    return kz_real_digits_text(negative, digits, n, k, fixed_max_scale, out);
}

void kz_text_f64(double value, kz_str *out)
{
    uint8_t *text = (uint8_t *)kz_alloc(32);
    out->ptr = text;
    out->len = kz_real_text(kz_f64_bits(value), 53, 11, KZ_FIXED_MAX_SCALE, text);
}

void kz_text_f32(float value, kz_str *out)
{
    uint8_t *text = (uint8_t *)kz_alloc(32);
    out->ptr = text;
    out->len = kz_real_text(kz_f32_bits(value), 24, 8, KZ_FIXED_MAX_SCALE, text);
}

/* ---- the floating-point remainder (T25): the exact `fmod`, as C#'s `%` on a double ------------- */

double kz_f64_rem(double a, double b)
{
    uint64_t ua = kz_f64_bits(a);
    uint64_t ub = kz_f64_bits(b);
    uint64_t sign = ua & ((uint64_t)1 << 63);
    int ea = (int)((ua >> 52) & 0x7FF);
    int eb = (int)((ub >> 52) & 0x7FF);
    uint64_t ma = ua & (((uint64_t)1 << 52) - 1);
    uint64_t mb = ub & (((uint64_t)1 << 52) - 1);
    if (ea == 0x7FF || eb == 0x7FF || (eb == 0 && mb == 0)) {
        if (ea != 0x7FF && eb == 0x7FF && mb == 0) {
            return a;                                /* a finite number by an infinity is itself */
        }
        return kz_f64_from_bits((uint64_t)0x7FF8000000000000); /* NaN */
    }
    if (ea == 0 && ma == 0) {
        return a;                                    /* a zero keeps its sign */
    }
    double absa = kz_f64_from_bits(ua & ~((uint64_t)1 << 63));
    double absb = kz_f64_from_bits(ub & ~((uint64_t)1 << 63));
    if (absa < absb) {
        return a;
    }
    if (ea == 0) {
        ea = 1;
    } else {
        ma |= (uint64_t)1 << 52;
    }
    if (eb == 0) {
        eb = 1;
    } else {
        mb |= (uint64_t)1 << 52;
    }
    ea -= 1075;
    eb -= 1075;
    uint64_t r = ma % mb;
    for (int i = ea; i > eb; i--) {
        r = (r << 1) % mb;
    }
    if (r == 0) {
        return kz_f64_from_bits(sign);
    }
    int e = eb;
    while (r < ((uint64_t)1 << 52)) {
        r <<= 1;
        e--;
    }
    int biased = e + 1075;
    if (biased >= 1) {
        return kz_f64_from_bits(sign | ((uint64_t)biased << 52) | (r & (((uint64_t)1 << 52) - 1)));
    }
    return kz_f64_from_bits(sign | (r >> (1 - biased)));   /* a subnormal result: exact, since the remainder is representable */
}

float kz_f32_rem(float a, float b)
{
    return (float)kz_f64_rem((double)a, (double)b);      /* the remainder of two floats is a float: the double computes it exactly */
}

/* ---- decimal (T2, T25, T26): the 96-bit mantissa, the scale and the sign of System.Decimal ----- */

/* Two words carry a decimal across the boundary: word 0 the low 64 bits of the mantissa, word 1 its high 32 bits, the scale in bits 32 to 39 and the sign in bit 63. */
typedef struct { uint64_t low64; uint32_t high; int scale; int negative; } kz_dec;

static kz_dec kz_dec_unpack(int64_t w0, int64_t w1)
{
    kz_dec d;
    d.low64 = (uint64_t)w0;
    d.high = (uint32_t)(uint64_t)w1;
    d.scale = (int)(((uint64_t)w1 >> 32) & 0xFF);
    d.negative = (int)((uint64_t)w1 >> 63);
    return d;
}

static void kz_dec_pack(const kz_dec *d, int64_t *out)
{
    out[0] = (int64_t)d->low64;
    out[1] = (int64_t)((uint64_t)d->high | ((uint64_t)d->scale << 32) | ((uint64_t)(d->negative ? 1 : 0) << 63));
}

static void kz_dec_big(const kz_dec *d, kz_big *b)
{
    kz_big_set96(b, d->low64, d->high);
}

static int kz_dec_is_zero(const kz_dec *d)
{
    return d->low64 == 0 && d->high == 0;
}

static int kz_big_fits96(const kz_big *b)
{
    return kz_big_bits(b) <= 96;
}

static void kz_dec_from_big(kz_dec *d, const kz_big *b, int scale, int negative)
{
    d->low64 = (b->n > 0 ? b->w[0] : 0) | ((uint64_t)(b->n > 1 ? b->w[1] : 0) << 32);
    d->high = b->n > 2 ? b->w[2] : 0;
    d->scale = scale;
    d->negative = negative;
}

/*
 * Cuts digits off a value until it fits 96 bits and a scale of 28 at most, rounding the rest half
 * to even, as System.Decimal does after a multiplication and after an addition it could not align
 * inside 96 bits; 0 when the value does not fit at the scale 0 either.
 */
static int kz_dec_fit(kz_big *v, int *scale)
{
    int last = 0;
    int sticky = 0;
    while (!kz_big_fits96(v) || *scale > 28) {
        if (*scale == 0) {
            return 0;
        }
        sticky |= last != 0;
        last = (int)kz_big_divmod_small(v, 10);
        (*scale)--;
    }
    if (last > 5 || (last == 5 && (sticky || (v->n > 0 && (v->w[0] & 1) != 0)))) {
        kz_big one;
        kz_big_set(&one, 1);
        kz_big_add(v, &one);
        if (!kz_big_fits96(v)) {
            if (*scale == 0) {
                return 0;
            }
            kz_big_divmod_small(v, 10);              /* 2^96 / 10 leaves .6: up */
            kz_big_add(v, &one);
            (*scale)--;
        }
    }
    return 1;
}

static void kz_dec_overflow(int32_t line)
{
    kz_fail("overflow", line);
}

static void kz_dec_add_sub(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int subtract, int32_t line, int64_t *out)
{
    kz_dec a = kz_dec_unpack(a0, a1);
    kz_dec b = kz_dec_unpack(b0, b1);
    int b_negative = b.negative ^ (subtract ? 1 : 0);
    int scale = a.scale > b.scale ? a.scale : b.scale;
    kz_big A, B;
    kz_dec_big(&a, &A);
    kz_dec_big(&b, &B);
    kz_big_mul_pow10(&A, scale - a.scale);
    kz_big_mul_pow10(&B, scale - b.scale);
    int subtracting = a.negative != b_negative;
    int negative;
    kz_big R;
    if (!subtracting) {
        kz_big_copy(&R, &A);
        kz_big_add(&R, &B);
        negative = a.negative;
    } else if (kz_big_cmp(&A, &B) >= 0) {
        kz_big_copy(&R, &A);
        kz_big_sub(&R, &B);
        negative = a.negative;
    } else {
        kz_big_copy(&R, &B);
        kz_big_sub(&R, &A);
        negative = b_negative;
    }
    if (R.n == 0) {
        negative = a.negative ^ (subtracting && a.scale > b.scale); /* the sign System.Decimal gives a zero difference: the first operand's, flipped where it was the one with the larger scale */
    }
    if (!kz_big_fits96(&R) && !kz_dec_fit(&R, &scale)) {
        kz_dec_overflow(line);
    }
    kz_dec r;
    kz_dec_from_big(&r, &R, scale, negative);
    kz_dec_pack(&r, out);
}

void kz_dec_add(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int32_t line, int64_t *out)
{
    kz_dec_add_sub(a0, a1, b0, b1, 0, line, out);
}

void kz_dec_sub(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int32_t line, int64_t *out)
{
    kz_dec_add_sub(a0, a1, b0, b1, 1, line, out);
}

void kz_dec_mul(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int32_t line, int64_t *out)
{
    kz_dec a = kz_dec_unpack(a0, a1);
    kz_dec b = kz_dec_unpack(b0, b1);
    int scale = a.scale + b.scale;
    int narrow = a.high == 0 && b.high == 0 && (a.low64 >> 32) == 0 && (b.low64 >> 32) == 0;
    kz_big A, B, P;
    kz_dec_big(&a, &A);
    kz_dec_big(&b, &B);
    kz_big_mul(&P, &A, &B);
    kz_dec r;
    if (narrow ? scale > 47 : P.n == 0) {
        /*
         * The plain zero, as System.Decimal returns it: two factors of 32 bits each whose scales
         * add up past 47, or a zero factor beside one wider than 32 bits. Every other zero keeps
         * the factors' sign and the scale below, 28 when the sum was cut down to it.
         */
        r.low64 = 0;
        r.high = 0;
        r.scale = 0;
        r.negative = 0;
        kz_dec_pack(&r, out);
        return;
    }
    if (!kz_dec_fit(&P, &scale)) {
        kz_dec_overflow(line);
    }
    kz_dec_from_big(&r, &P, scale, a.negative ^ b.negative);
    kz_dec_pack(&r, out);
}

/* Whether a quotient is at most 2^96 / 10, so that it takes another digit, which may then still carry past 96 bits. */
static int kz_big_takes_digit(const kz_big *q)
{
    kz_big limit;
    kz_big_set96(&limit, 0x9999999999999999ull, 0x19999999u);
    return kz_big_cmp(q, &limit) <= 0;
}

void kz_dec_div(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int32_t line, int64_t *out)
{
    kz_dec a = kz_dec_unpack(a0, a1);
    kz_dec b = kz_dec_unpack(b0, b1);
    if (kz_dec_is_zero(&b)) {
        kz_fail("divide-by-zero", line);
    }
    int scale = a.scale - b.scale;
    kz_big A, B, Q, R;
    kz_dec_big(&a, &A);
    kz_dec_big(&b, &B);
    if (scale < 0) {
        kz_big_mul_pow10(&A, -scale);
        scale = 0;
    }
    kz_big_divmod(&Q, &R, &A, &B);
    if (!kz_big_fits96(&Q)) {
        kz_dec_overflow(line);
    }
    int appended = R.n != 0;                         /* digits beyond the natural scale: a trailing zero among them is dropped, as System.Decimal does */
    int carried = 0;
    while (R.n != 0 && scale < 28 && kz_big_takes_digit(&Q)) {
        kz_big_mul_small(&R, 10);
        int d = 0;
        while (kz_big_cmp(&R, &B) >= 0) {
            kz_big_sub(&R, &B);
            d++;
        }
        kz_big_mul_small(&Q, 10);
        kz_big digit;
        kz_big_set(&digit, (uint64_t)d);
        kz_big_add(&Q, &digit);
        scale++;
        if (!kz_big_fits96(&Q)) {
            /* the digit carried past 96 bits: it comes off again, rounding half to even with what is left */
            uint32_t last = kz_big_divmod_small(&Q, 10);
            scale--;
            if (last > 5 || (last == 5 && (R.n != 0 || (Q.w[0] & 1) != 0))) {
                kz_big one;
                kz_big_set(&one, 1);
                kz_big_add(&Q, &one);
            }
            carried = 1;
            break;
        }
    }
    if (R.n != 0 && !carried) {
        kz_big twice;
        kz_big_copy(&twice, &R);
        kz_big_shl(&twice, 1);
        int c = kz_big_cmp(&twice, &B);
        if (c > 0 || (c == 0 && (Q.n > 0 && (Q.w[0] & 1) != 0))) {
            kz_big one;
            kz_big_set(&one, 1);
            kz_big_add(&Q, &one);
            if (!kz_big_fits96(&Q)) {
                if (scale == 0) {
                    kz_dec_overflow(line);
                }
                kz_big_divmod_small(&Q, 10);
                kz_big_add(&Q, &one);
                scale--;
            }
        }
    }
    if (appended) {
        for (;;) {
            if (scale == 0) {
                break;
            }
            kz_big tenth;
            kz_big_copy(&tenth, &Q);
            if (kz_big_divmod_small(&tenth, 10) != 0) {
                break;
            }
            kz_big_copy(&Q, &tenth);
            scale--;
        }
    }
    kz_dec r;
    kz_dec_from_big(&r, &Q, scale, a.negative ^ b.negative);
    kz_dec_pack(&r, out);
}

void kz_dec_rem(int64_t a0, int64_t a1, int64_t b0, int64_t b1, int32_t line, int64_t *out)
{
    kz_dec a = kz_dec_unpack(a0, a1);
    kz_dec b = kz_dec_unpack(b0, b1);
    if (kz_dec_is_zero(&b)) {
        kz_fail("divide-by-zero", line);
    }
    int scale = a.scale > b.scale ? a.scale : b.scale;
    kz_big A, B, Q, R;
    kz_dec_big(&a, &A);
    kz_dec_big(&b, &B);
    kz_big_mul_pow10(&A, scale - a.scale);
    kz_big_mul_pow10(&B, scale - b.scale);
    int c = kz_big_cmp(&A, &B);
    if (c < 0) {
        out[0] = a0;                                 /* below the divisor: the dividend as it is */
        out[1] = a1;
        return;
    }
    kz_dec r;
    if (c == 0) {
        R.n = 0;
        kz_dec_from_big(&r, &R, scale, a.negative);
        kz_dec_pack(&r, out);
        return;
    }
    kz_big_divmod(&Q, &R, &A, &B);
    if (!kz_big_fits96(&R) && !kz_dec_fit(&R, &scale)) {
        kz_dec_overflow(line);
    }
    kz_dec_from_big(&r, &R, scale, a.negative);
    kz_dec_pack(&r, out);
}

int32_t kz_dec_cmp(int64_t a0, int64_t a1, int64_t b0, int64_t b1)
{
    kz_dec a = kz_dec_unpack(a0, a1);
    kz_dec b = kz_dec_unpack(b0, b1);
    int sa = kz_dec_is_zero(&a) ? 0 : (a.negative ? -1 : 1);
    int sb = kz_dec_is_zero(&b) ? 0 : (b.negative ? -1 : 1);
    if (sa != sb) {
        return sa < sb ? -1 : 1;
    }
    if (sa == 0) {
        return 0;
    }
    int scale = a.scale > b.scale ? a.scale : b.scale;
    kz_big A, B;
    kz_dec_big(&a, &A);
    kz_dec_big(&b, &B);
    kz_big_mul_pow10(&A, scale - a.scale);
    kz_big_mul_pow10(&B, scale - b.scale);
    int c = kz_big_cmp(&A, &B);
    return sa < 0 ? -c : c;
}

void kz_dec_from_i64(int64_t value, int64_t *out)
{
    kz_dec d;
    d.negative = value < 0;
    d.low64 = value < 0 ? (uint64_t)0 - (uint64_t)value : (uint64_t)value;
    d.high = 0;
    d.scale = 0;
    kz_dec_pack(&d, out);
}

void kz_dec_from_u64(uint64_t value, int64_t *out)
{
    kz_dec d;
    d.negative = 0;
    d.low64 = value;
    d.high = 0;
    d.scale = 0;
    kz_dec_pack(&d, out);
}

/* The whole part of a decimal, toward zero, as an unsigned magnitude; 0 when it does not fit 64 bits. */
static int kz_dec_whole(const kz_dec *d, uint64_t *magnitude)
{
    kz_big b;
    kz_dec_big(d, &b);
    for (int i = 0; i < d->scale; i++) {
        kz_big_divmod_small(&b, 10);
    }
    if (kz_big_bits(&b) > 64) {
        return 0;
    }
    *magnitude = (b.n > 0 ? b.w[0] : 0) | ((uint64_t)(b.n > 1 ? b.w[1] : 0) << 32);
    return 1;
}

/* T26: the value toward zero as a `long`; `fits` 0 and the nearest end of the range when it lies outside. */
int64_t kz_dec_to_i64(int64_t w0, int64_t w1, int8_t *fits)
{
    kz_dec d = kz_dec_unpack(w0, w1);
    uint64_t magnitude;
    int ok = kz_dec_whole(&d, &magnitude);
    if (d.negative) {
        if (ok && magnitude <= (uint64_t)1 << 63) {
            *fits = 1;
            return (int64_t)((uint64_t)0 - magnitude);
        }
        *fits = 0;
        return INT64_MIN;
    }
    if (ok && magnitude <= (uint64_t)INT64_MAX) {
        *fits = 1;
        return (int64_t)magnitude;
    }
    *fits = 0;
    return INT64_MAX;
}

uint64_t kz_dec_to_u64(int64_t w0, int64_t w1, int8_t *fits)
{
    kz_dec d = kz_dec_unpack(w0, w1);
    uint64_t magnitude;
    int ok = kz_dec_whole(&d, &magnitude);
    if (d.negative) {
        if (ok && magnitude == 0) {
            *fits = 1;
            return 0;
        }
        *fits = 0;
        return 0;
    }
    if (ok) {
        *fits = 1;
        return magnitude;
    }
    *fits = 0;
    return UINT64_MAX;
}

static const double kz_pow10_f64[29] = {
    1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19,
    1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28,
};

/* As System.Decimal converts to a double: the mantissa as a double, divided by the power of ten of the scale. */
double kz_dec_to_f64(int64_t w0, int64_t w1)
{
    kz_dec d = kz_dec_unpack(w0, w1);
    double value = ((double)d.low64 + (double)d.high * 18446744073709551616.0) / kz_pow10_f64[d.scale];
    return d.negative ? -value : value;
}

static int kz_floor_div(int value, int divisor)
{
    int q = value / divisor;
    if ((value % divisor != 0) && ((value < 0) != (divisor < 0))) {
        q--;
    }
    return q;
}

static void kz_dec_saturate(int negative, kz_dec *d)
{
    d->low64 = UINT64_MAX;
    d->high = UINT32_MAX;
    d->scale = 0;
    d->negative = negative;
}

/*
 * As System.Decimal converts from a double: the value is rounded to its 15 significant digits
 * (7 for a float) as an integer at the scale that holds them, which then takes the decimal's scale
 * with its trailing zeros dropped; `fits` 0 and the end of the range for a value beyond it or an
 * infinity, and 0 for NaN (T26).
 */
static void kz_dec_from_real(double value, int digits, int8_t *fits, int64_t *out)
{
    kz_dec d;
    uint64_t bits = kz_f64_bits(value);
    int biased = (int)((bits >> 52) & 0x7FF);
    int exp = biased - 1022;
    *fits = 1;
    if (biased == 0x7FF) {
        *fits = 0;
        if ((bits & (((uint64_t)1 << 52) - 1)) != 0) {
            d.low64 = 0;
            d.high = 0;
            d.scale = 0;
            d.negative = 0;
        } else {
            kz_dec_saturate((int)(bits >> 63), &d);
        }
        kz_dec_pack(&d, out);
        return;
    }
    d.low64 = 0;
    d.high = 0;
    d.scale = 0;
    d.negative = 0;
    if (exp < -94) {
        kz_dec_pack(&d, out);
        return;
    }
    int negative = value < 0;
    if (exp > 96) {
        *fits = 0;
        kz_dec_saturate(negative, &d);
        kz_dec_pack(&d, out);
        return;
    }
    double dbl = negative ? -value : value;
    double full = digits == 15 ? 1e15 : 1e7;
    double tenth = digits == 15 ? 1e14 : 1e6;
    int power = (digits - 1) - kz_floor_div(exp * 19728, 65536);
    if (power >= 0) {
        if (power > 28) {
            power = 28;
        }
        dbl *= kz_pow10_f64[power];
    } else if (power != -1 || dbl >= full) {
        dbl /= kz_pow10_f64[-power];
    } else {
        power = 0;
    }
    if (dbl < tenth && power < 28) {
        dbl *= 10;
        power++;
    }
    uint64_t mant = (uint64_t)dbl;
    double rest = dbl - (double)mant;
    if (rest > 0.5 || (rest == 0.5 && (mant & 1) != 0)) {
        mant++;
    }
    if (mant == 0) {
        kz_dec_pack(&d, out);
        return;
    }
    if (power < 0) {
        kz_big b;
        kz_big_set(&b, mant);
        kz_big_mul_pow10(&b, -power);
        if (!kz_big_fits96(&b)) {
            *fits = 0;
            kz_dec_saturate(negative, &d);
            kz_dec_pack(&d, out);
            return;
        }
        kz_dec_from_big(&d, &b, 0, negative);
        kz_dec_pack(&d, out);
        return;
    }
    int lmax = power < digits - 1 ? power : digits - 1;
    while (lmax > 0 && mant % 10 == 0) {
        mant /= 10;
        power--;
        lmax--;
    }
    d.low64 = mant;
    d.high = 0;
    d.scale = power;
    d.negative = negative;
    kz_dec_pack(&d, out);
}

void kz_dec_from_f64(double value, int8_t *fits, int64_t *out)
{
    kz_dec_from_real(value, 15, fits, out);
}

void kz_dec_from_f32(float value, int8_t *fits, int64_t *out)
{
    kz_dec_from_real((double)value, 7, fits, out);  /* the double of a float has the float's exponent, so the same steps hold with 7 digits */
}

/* The text of a decimal (A2): its digits with the point `scale` places from the right, `0.` before a value below one. */
void kz_text_dec(int64_t w0, int64_t w1, kz_str *out)
{
    kz_dec d = kz_dec_unpack(w0, w1);
    uint8_t *text = (uint8_t *)kz_alloc(32);
    uint8_t digits[32];
    int n = 0;
    kz_big b;
    kz_dec_big(&d, &b);
    while (b.n != 0) {
        digits[n++] = (uint8_t)kz_big_divmod_small(&b, 10);
    }
    while (n <= d.scale) {
        digits[n++] = 0;                             /* at least one digit before the point */
    }
    int64_t len = 0;
    if (d.negative && !kz_dec_is_zero(&d)) {
        text[len++] = '-';
    }
    for (int i = n - 1; i >= 0; i--) {
        if (i == d.scale - 1) {
            text[len++] = '.';
        }
        text[len++] = (uint8_t)('0' + digits[i]);
    }
    out->ptr = text;
    out->len = len;
}

/* ---- the text of a duration (A12) ---------------------------------------------------------- */

typedef struct { const char *name; uint64_t nanos; } kz_unit;

static const kz_unit kz_units[7] = {
    { "days", 86400000000000ull }, { "h", 3600000000000ull }, { "min", 60000000000ull }, { "s", 1000000000ull },
    { "ms", 1000000ull }, { "us", 1000ull }, { "ns", 1ull },
};

/* The units from `days` down with a nonzero count, each once, `0ms` for zero, `-` before a negative whole. */
static int64_t kz_duration_text(int negative, kz_big *magnitude, uint8_t *text)
{
    int64_t len = 0;
    if (magnitude->n == 0) {
        static const uint8_t zero[] = "0ms";
        kz_copy(text, zero, 3);
        return 3;
    }
    if (negative) {
        text[len++] = '-';
    }
    int any = 0;
    for (int u = 0; u < 7; u++) {
        kz_big unit, count, rest;
        kz_big_set(&unit, kz_units[u].nanos);
        kz_big_divmod(&count, &rest, magnitude, &unit);
        kz_big_copy(magnitude, &rest);
        if (count.n == 0) {
            continue;
        }
        if (any) {
            text[len++] = ' ';
        }
        any = 1;
        uint8_t digits[40];
        int n = 0;
        while (count.n != 0) {
            digits[n++] = (uint8_t)kz_big_divmod_small(&count, 10);
        }
        for (int i = n - 1; i >= 0; i--) {
            text[len++] = (uint8_t)('0' + digits[i]);
        }
        int64_t m = kz_cstr_len(kz_units[u].name);
        kz_copy(text + len, kz_units[u].name, m);
        len += m;
    }
    return len;
}

void kz_text_duration(int64_t nanos, kz_str *out)
{
    uint8_t *text = (uint8_t *)kz_alloc(64);
    kz_big magnitude;
    kz_big_set(&magnitude, nanos < 0 ? (uint64_t)0 - (uint64_t)nanos : (uint64_t)nanos);
    out->ptr = text;
    out->len = kz_duration_text(nanos < 0, &magnitude, text);
}

void kz_text_longduration(int64_t low, int64_t high, kz_str *out)
{
    uint8_t *text = (uint8_t *)kz_alloc(96);
    uint64_t lo = (uint64_t)low;
    uint64_t hi = (uint64_t)high;
    int negative = high < 0;
    if (negative) {
        lo = ~lo;
        hi = ~hi;
        if (++lo == 0) {
            hi++;
        }
    }
    kz_big magnitude;
    magnitude.w[0] = (uint32_t)lo;
    magnitude.w[1] = (uint32_t)(lo >> 32);
    magnitude.w[2] = (uint32_t)hi;
    magnitude.w[3] = (uint32_t)(hi >> 32);
    magnitude.n = 4;
    kz_big_norm(&magnitude);
    out->ptr = text;
    out->len = kz_duration_text(negative, &magnitude, text);
}

void kz_concat(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen, kz_str *out)
{
    if (alen == 0) {
        out->ptr = b;
        out->len = blen;
        return;
    }
    if (blen == 0) {
        out->ptr = a;
        out->len = alen;
        return;
    }
    uint8_t *bytes = (uint8_t *)kz_alloc(alen + blen);
    kz_copy(bytes, a, alen);
    kz_copy(bytes + alen, b, blen);
    out->ptr = bytes;
    out->len = alen + blen;
}

int8_t kz_str_eq(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen)
{
    return alen == blen && kz_same(a, b, alen);
}

/* ---- lists (M9) ------------------------------------------------------------------------------ */

static kz_buf *kz_buf_new(int64_t cap, int64_t size)
{
    kz_buf *buf = (kz_buf *)kz_alloc((int64_t)sizeof(kz_buf) + cap * size);
    buf->cap = cap;
    buf->filled = 0;
    return buf;
}

static uint8_t *kz_items(kz_buf *buf)
{
    return (uint8_t *)buf + sizeof(kz_buf);
}

/*
 * Appends to the list value in place (M9's `Add` on a `mut` variable). The buffer is written in
 * place when this value holds its last item, which no other value can then read; otherwise the
 * items are copied into a buffer of their own.
 */
void kz_list_push(kz_list *list, const void *item, int64_t size)
{
    kz_buf *buf = list->buf;
    int64_t count = list->count;
    if (buf == NULL || count != buf->filled || buf->filled == buf->cap) {
        int64_t cap = buf == NULL ? 4 : (count * 2 > 4 ? count * 2 : 4);
        kz_buf *grown = kz_buf_new(cap, size);
        if (count > 0) {
            kz_copy(kz_items(grown), kz_items(buf), count * size);
        }
        grown->filled = count;
        buf = grown;
    }
    kz_copy(kz_items(buf) + count * size, item, size);
    buf->filled = count + 1;
    list->buf = buf;
    list->count = count + 1;
}

/* The place of the item at `index`, or the exception `index-out-of-range` at the index's line (T14). */
void *kz_list_at(kz_buf *buf, int64_t count, int64_t index, int64_t size, int32_t line)
{
    if (index < 0 || index >= count) {
        kz_fail("index-out-of-range", line);
    }
    return kz_items(buf) + index * size;
}

/* A string's `Bytes`: its UTF-8 bytes as a list of `byte` (T15). */
void kz_str_bytes(const uint8_t *s, int64_t len, kz_list *out)
{
    out->buf = NULL;
    out->count = 0;
    if (len == 0) {
        return;
    }
    kz_buf *buf = kz_buf_new(len, 1);
    kz_copy(kz_items(buf), s, len);
    buf->filled = len;
    out->buf = buf;
    out->count = len;
}

/* A string's `Chars`: its code points as a list of `char` (T15). A byte sequence that is not UTF-8 is read one byte at a time. */
void kz_str_chars(const uint8_t *s, int64_t len, kz_list *out)
{
    out->buf = NULL;
    out->count = 0;
    if (len == 0) {
        return;
    }
    kz_buf *buf = kz_buf_new(len, 4);
    int32_t *items = (int32_t *)kz_items(buf);
    int64_t n = 0;
    for (int64_t i = 0; i < len;) {
        uint8_t b = s[i];
        int64_t more = b < 0x80 ? 0 : (b & 0xE0) == 0xC0 ? 1 : (b & 0xF0) == 0xE0 ? 2 : (b & 0xF8) == 0xF0 ? 3 : -1;
        if (more < 0) {
            items[n++] = b;
            i++;
            continue;
        }
        uint32_t cp = more == 0 ? b : more == 1 ? (b & 0x1Fu) : more == 2 ? (b & 0x0Fu) : (b & 0x07u);
        int64_t j = 1;
        for (; j <= more && i + j < len && (s[i + j] & 0xC0) == 0x80; j++) {
            cp = (cp << 6) | (s[i + j] & 0x3Fu);
        }
        if (j <= more) {
            items[n++] = b;                          /* a truncated sequence: the lead byte stands for itself */
            i++;
            continue;
        }
        items[n++] = (int32_t)cp;
        i += more + 1;
    }
    buf->filled = n;
    out->buf = buf;
    out->count = n;
}

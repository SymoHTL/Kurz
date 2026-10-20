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

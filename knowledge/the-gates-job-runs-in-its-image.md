---
name: the-gates-job-runs-in-its-image
description: Recipe for a change to the image the gates job runs in (edit ci/Dockerfile, push, take the digest the image run prints into gates.yml, push again) and for a local run of the gates in the same image (2026-10-22)
metadata:
  type: reference
---

The `gates` job runs in the image `ci/Dockerfile` defines, named in `.github/workflows/gates.yml`
by its digest (the owner, 2026-10-22, "Public GHCR image"). Section 2 of the design record is why:
the LLVM major the compiler accepts comes from the packages LLVM publishes where the distribution
has none of it, installed when an image is built and not on every run.

A change to the image:

1. Edit `ci/Dockerfile`. `tools/lint_ci.py` holds its facts: the base named by its digest, the SDK
   that `compiler/global.json` pins, each download checked by its hash or its key.
2. Push the branch. `.github/workflows/image.yml` runs on a push that changes the file, on any
   branch, publishes `ghcr.io/symohtl/kurz-ci:<commit sha>`, and its last line prints
   `ghcr.io/symohtl/kurz-ci@sha256:<digest>`.
3. Write that digest into `container.image` of `gates.yml` and push again. The `gates` run of the
   first push runs in the image the previous digest names; the second push's run is the first in
   the new one.

A digest names one image for good: a newer base, a newer LLVM point release or a newer package of
the distribution reaches the gates job only through these three steps.

A local run of the gates in the same image, from the root of a clone, with Docker:

    docker build -t kurz-ci ci
    docker run --rm -v "$PWD":/w -w /w kurz-ci sh -c 'git config --global --add safe.directory /w && python3 -m venv /tmp/v && /tmp/v/bin/pip install -q --require-hashes -r tools/review/requirements.txt && /tmp/v/bin/python tools/gates.py'

The local image is built from the same file, not pulled: the same file is not the same bytes, since
the distribution and LLVM's packages give what they hold on the day of the build. The gates that
read the forge need a gh login inside the container: without one `merge-checks` is FAIL, its
`gh repo view` refused, and the three pull-request gates are NOT RUN as on the host without `--pr`.

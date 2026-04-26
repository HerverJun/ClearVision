# Frequency Golden Runner Template

This note captures the FFT1D runner decisions that should be reused for
frequency-domain operators.

## Case Shape

- Keep generated cases under `quality/synthetic/cases/<slice>/`; this directory
  is ignored and can be regenerated.
- Commit the generator, runner, metric helper, baseline JSON, Markdown report,
  and any failure triage.
- Store benchmark evidence in every baseline JSON through `RuntimeMsAvg` and
  `MemoryAllocationBytesAvg`, because `operator_quality_matrix.md` infers
  `HasBenchmark` from those fields.

## Numeric Contracts

- For real-valued FFT inputs, magnitude is conjugate symmetric:
  `|X[k]| == |X[N-k]|`. Dominant-bin checks must accept either side when the
  implementation and numpy differ only by floating-point tie-breaking.
- Keep generator metadata from overwriting computed oracle values. Derived
  values from numpy/OpenCV or another oracle should win over scenario labels.
- Validate finite outputs before comparing scalar tolerances so NaN/Infinity
  failures remain obvious.
- For inverse transforms, compare both real and imaginary channels. Real-signal
  round trips should have negligible imaginary residue, while deliberately
  complex spectra should preserve the expected imaginary channel.

## ImageWrapper Ownership

- Release wrappers created by the runner and wrappers returned by the operator.
- Do not release input dictionaries wholesale. The runner, upstream operator,
  and downstream operator may share an `ImageWrapper` reference in chained
  frequency tests.
- When chaining FFT -> IFFT inside a runner, keep the FFT spectrum alive until
  the inverse operator finishes, then release all distinct wrappers once.

## Report Pattern

- Use one `Operators` entry per operator with case count, pass/fail count,
  average/max runtime, and average allocation.
- Markdown reports should stay concise: summary, operator table, and failure
  table only when failures exist.
- Re-run `quality/tools/generate_operator_quality_matrix.py` after writing a
  baseline so the matrix picks up status and case count automatically.

# G16/U07 target-machine evidence kit

This kit creates one independently closable evidence record per target SKU/profile. It does not change Windows display scale, does not run a release, and never sets `releaseEligible` to true.

## Target-machine workflow

1. Copy the portable ZIP, this collector, the observation template, and the JSON schema outside the product runtime directory.
2. Copy `profile-observation.template.json` to a new operator-owned file. Keep every unexecuted item at `NOT_RUN`; only enter measurements and PASS/FAIL/BLOCKED/INCONCLUSIVE after the corresponding manual target-machine step actually ran.
3. Run the collector once for that profile. Supply the expected portable ZIP SHA-256 and the implementation Git SHA/dirty state from the release candidate. The resolution and OS scale parameters bind operator-observed target settings; the collector never writes those settings.
4. Pass screenshots with `-ScreenshotPath` and logs with `-LogPath`. The collector copies them under the evidence directory and records their SHA-256 hashes in both `evidence.json` and `SHA256SUMS`.
5. Review `evidence.json`, then have the real operator/reviewer fill the sign-off fields in the observation file and run the collector into a new output directory. Do not reuse or overwrite an existing evidence directory.

Example:

```powershell
& ".\collect-g16-u07-target-evidence.ps1" `
  -PackageZip ".\ClearVision-Studio-win-x64.zip" `
  -ExpectedPackageSha256 "<64 lowercase hex>" `
  -GitSha "<implementation SHA>" `
  -GitDirty:$false `
  -TargetSku "Studio-Production" `
  -TargetProfile "win11-1920x1080-125-webview2-evergreen" `
  -ProfileClass formal-sku `
  -ResolutionWidth 1920 `
  -ResolutionHeight 1080 `
  -OsScalePercent 125 `
  -OperatorProfile "production-operator-set" `
  -ModelProfile "approved-model-set-v1" `
  -DeviceProfile "station-camera-plc-profile-a" `
  -ObservationPath ".\profile-observation.executed.json" `
  -ScreenshotPath ".\screens\startup.png", ".\screens\agent-workspace.png" `
  -LogPath ".\logs\studio.log" `
  -OutputDirectory ".\evidence\studio-production-1920x1080-125"
```

The 300- and 1000-primitive rows record input-to-paint and RAF p50/p95 plus long-frame counts. Working set, Project save/load, legacy project/package, Station reconnect/result, and Agent workspace have independent statuses and evidence references.

`fixture=true` is reserved for self-tests and cannot be treated as target evidence. Reviewer, device serial number, approval decision, and operator identity are null by default and are never inferred. A formal SKU profile is release-blocking; an experimental profile is not automatically made release-blocking, and closing one profile does not close any other profile.

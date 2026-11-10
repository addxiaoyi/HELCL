[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$ExpectedSubject
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Authenticode verification requires Windows.' }
$entry = Get-Item -LiteralPath $Path
$files = if ($entry.PSIsContainer) {
    @('HelL.Desktop.exe','HelL.Desktop.dll','HelL.Core.dll') | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $entry.FullName $_)
    }
} else { @($entry) }
foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid') { throw "Invalid signature on $($file.Name): $($signature.Status)" }
    if ($signature.SignerCertificate.Subject -cne $ExpectedSubject) {
        throw "Unexpected signer on $($file.Name). Expected the configured certificate subject."
    }
    if (-not $signature.TimeStamperCertificate) { throw "Missing trusted timestamp on $($file.Name)." }
    Write-Output "Verified Authenticode and timestamp: $($file.Name)"
}

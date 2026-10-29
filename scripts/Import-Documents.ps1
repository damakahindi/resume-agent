param(
    [Parameter(Mandatory)]
    [string] $Folder
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $Folder -PathType Container)) {
    throw 'Select an existing private document folder.'
}
$files = Get-ChildItem -LiteralPath $Folder -File |
    Where-Object { $_.Extension.ToLowerInvariant() -in '.pdf', '.docx', '.txt', '.md' }
$count = 0
foreach ($file in $files) {
    Invoke-RestMethod -Uri 'http://127.0.0.1:5283/api/documents' -Method Post `
        -Headers @{ 'X-Resume-Agent' = 'local' } -Form @{ file = $file } | Out-Null
    $count++
}
Write-Output "Imported $count document(s) into the local private knowledge store."

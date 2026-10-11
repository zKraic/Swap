$ErrorActionPreference = 'Stop'
$path = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PATH__'))
$s = Get-AuthenticodeSignature -LiteralPath $path
@{ Valid=($s.Status -eq 'Valid'); Subject=[string]$s.SignerCertificate.Subject; Status=[string]$s.Status } | ConvertTo-Json -Compress

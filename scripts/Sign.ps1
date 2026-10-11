# KraicDev. Sign a separate release only with an existing trusted code-signing identity.
param(
 [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
 [ValidateSet('CurrentUser','LocalMachine')][string]$Store='CurrentUser',
 [string]$InputExe='dist/Swap.exe',
 [string]$OutputExe='dist/Swap-signed.exe',
 [string]$SignTool,
 [uri]$TimestampServer='http://timestamp.digicert.com'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$inputPath=[IO.Path]::GetFullPath((Join-Path $root $InputExe))
$outputPath=[IO.Path]::GetFullPath((Join-Path $root $OutputExe))
if($inputPath-eq$outputPath-or(Test-Path -LiteralPath $outputPath)){throw 'Use a new output path. The validated unsigned delivery is preserved.'}
if(!(Test-Path -LiteralPath $inputPath -PathType Leaf)-or[IO.Path]::GetExtension($inputPath)-ne'.exe'){throw 'Input must be an existing EXE.'}
if((Get-Item -LiteralPath $inputPath).VersionInfo.CompanyName-ne'KraicDev'){throw 'The input is not the KraicDev Swap delivery.'}
if($TimestampServer.Scheme-notin'http','https'-or!$TimestampServer.IsAbsoluteUri-or$TimestampServer.UserInfo){throw 'Invalid timestamp endpoint.'}
$certificate=Get-Item -LiteralPath ('Cert:\'+$Store+'\My\'+$CertificateThumbprint) -ErrorAction Stop
if(!$certificate.HasPrivateKey-or$certificate.NotAfter-lt(Get-Date)-or$certificate.NotBefore-gt(Get-Date)){throw 'The existing signing certificate/private key must be valid.'}
if(!@($certificate.EnhancedKeyUsageList|Where-Object{$_.ObjectId-eq'1.3.6.1.5.5.7.3.3'}).Count){throw 'The certificate must explicitly allow code signing.'}
$chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
try { $chain.ChainPolicy.RevocationMode='Online'; if(!$chain.Build($certificate)){throw ('Signing identity is not trusted: '+(($chain.ChainStatus|ForEach-Object Status)-join', '))} } finally {$chain.Dispose()}
if(!$SignTool){
 $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
 $SignTool=Get-ChildItem -LiteralPath $sdk -Directory -ErrorAction SilentlyContinue|Sort-Object Name -Descending|ForEach-Object{Join-Path $_.FullName 'x64/signtool.exe'}|Where-Object{Test-Path -LiteralPath $_}|Select-Object -First 1
}
if(!$SignTool-or!(Test-Path -LiteralPath $SignTool -PathType Leaf)){throw 'SignTool from the Windows SDK is needed on the release machine; end users do not need the SDK.'}
$toolSignature=Get-AuthenticodeSignature -LiteralPath $SignTool
if($toolSignature.Status-ne'Valid'-or$toolSignature.SignerCertificate.Subject-notmatch'Microsoft'){throw 'Use the official Microsoft SignTool.'}
[void][IO.Directory]::CreateDirectory((Split-Path $outputPath -Parent))
[IO.File]::Copy($inputPath,$outputPath,$false)
$signArguments=@('sign','/sha1',$CertificateThumbprint,'/s','My','/fd','SHA256','/tr',$TimestampServer.AbsoluteUri,'/td','SHA256','/d','Swap by KraicDev')
if($Store-eq'LocalMachine'){$signArguments+='/sm'}
$signArguments+=$outputPath
& $SignTool @signArguments
if($LASTEXITCODE-ne0){throw 'Signing failed. The staging output must not be published.'}
& $SignTool verify /pa /all /v $outputPath
if($LASTEXITCODE-ne0){throw 'Authenticode verification failed. Do not publish the staging output.'}
$signature=Get-AuthenticodeSignature -LiteralPath $outputPath
if($signature.Status-ne'Valid'-or$signature.SignerCertificate.Thumbprint-ne$certificate.Thumbprint-or!$signature.TimeStamperCertificate){throw 'A trusted signature and timestamp are required.'}
$hash=(Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
($hash+'  '+[IO.Path]::GetFileName($outputPath))|Set-Content -LiteralPath ($outputPath+'.sha256') -Encoding UTF8
@{author='KraicDev';signed=$true;publisher=$signature.SignerCertificate.Subject;thumbprint=$signature.SignerCertificate.Thumbprint;sha256=$hash;timestamped=$true;sourceSha256=(Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash}|ConvertTo-Json|Set-Content -LiteralPath ($outputPath+'.signing.json') -Encoding UTF8
Write-Output ('KraicDev: verified signed release: '+$outputPath)

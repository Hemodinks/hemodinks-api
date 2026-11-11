<#
.SYNOPSIS
    Gera o material de chave da protecao de CPF em repouso (#227) para UM ambiente.

.DESCRIPTION
    Cria, numa pasta fora do repositorio:
      cpf.pfx                  certificado autoassinado RSA 3072, validade de 10 anos (backup)
      CertificateBase64.txt    -> CpfProtection__CertificateBase64
      CertificatePassword.txt  -> CpfProtection__CertificatePassword
      LookupHashKey.txt        -> CpfProtection__LookupHashKey (32 bytes aleatorios)
      LEIA-ME.txt              para onde vai cada arquivo, inclusive na rotacao

    Nenhum valor e impresso na tela: so os caminhos e a impressao digital do certificado,
    que e publica. Rode numa maquina confiavel, uma vez por ambiente, e guarde a pasta num
    cofre de senhas antes de configurar o ambiente. Depois apague a pasta local.

.EXAMPLE
    pwsh ./scripts/New-CpfProtectionKeys.ps1 -Environment Production
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Production", "Confirmation", "Development")]
    [string]$Environment,

    [string]$OutputDirectory = (Join-Path (Join-Path ([Environment]::GetFolderPath("UserProfile")) "hemodinks-cpf-keys") ("{0}-{1:yyyyMMdd-HHmmss}" -f $Environment.ToLowerInvariant(), [DateTime]::UtcNow))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Use PowerShell 7 ou superior (pwsh)."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
$repositoryWithSeparator = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($outputFullPath.StartsWith($repositoryWithSeparator, [StringComparison]::OrdinalIgnoreCase) -or $outputFullPath -eq $repositoryRoot) {
    throw "A pasta de saida nao pode ficar dentro do repositorio."
}
if (Test-Path -LiteralPath $outputFullPath) {
    throw "A pasta de saida ja existe; escolha outra para nao sobrescrever material de chave."
}

$rsa = $null
$certificate = $null
$reloaded = $null
$pfx = $null
$hashKey = $null
$passwordBytes = $null
try {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=hemodinks-cpf-protection-$($Environment.ToLowerInvariant())",
        $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $now = [DateTimeOffset]::UtcNow
    $certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddYears(10))

    $passwordBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $password = [Convert]::ToBase64String($passwordBytes)
    $pfx = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password)
    $hashKey = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)

    # Mesma leitura que a API faz no startup: falha aqui em vez de falhar no deploy.
    $reloaded = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12(
        $pfx, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    $privateKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($reloaded)
    if ($null -eq $privateKey) {
        throw "O PFX gerado nao tem chave privada RSA."
    }
    $privateKey.Dispose()

    New-Item -ItemType Directory -Path $outputFullPath | Out-Null
    if (-not $IsWindows) {
        & chmod 700 $outputFullPath
    }

    $files = [ordered]@{
        "cpf.pfx"                 = $null
        "CertificateBase64.txt"   = [Convert]::ToBase64String($pfx)
        "CertificatePassword.txt" = $password
        "LookupHashKey.txt"       = [Convert]::ToBase64String($hashKey)
    }
    [IO.File]::WriteAllBytes((Join-Path $outputFullPath "cpf.pfx"), $pfx)
    foreach ($name in @($files.Keys | Where-Object { $null -ne $files[$_] })) {
        [IO.File]::WriteAllText((Join-Path $outputFullPath $name), $files[$name], [Text.UTF8Encoding]::new($false))
    }

    $readme = @"
Protecao de CPF em repouso (#227) - ambiente $Environment
Gerado em $($now.ToString("u")). Certificado: $($certificate.Subject), impressao digital $($certificate.Thumbprint), valido ate $($certificate.NotAfter.ToString("yyyy-MM-dd")).

Primeira configuracao (variaveis do ambiente, como secret):
  CertificateBase64.txt    -> CpfProtection__CertificateBase64
  CertificatePassword.txt  -> CpfProtection__CertificatePassword
  LookupHashKey.txt        -> CpfProtection__LookupHashKey

Rotacao (gere uma pasta nova; os valores ATUAIS do ambiente passam a ser os anteriores):
  certificado atual  -> CpfProtection__PreviousCertificates__0__CertificateBase64
  senha atual        -> CpfProtection__PreviousCertificates__0__CertificatePassword
  chave de hash atual-> CpfProtection__PreviousLookupHashKeys__0
  e os arquivos desta pasta entram como os novos valores atuais.
  Retire os anteriores so quando o log do startup mostrar 0 cifras pendentes de recifragem
  e 0 falhas na reconciliacao. Passo a passo: docs/issue-227-cpf-em-repouso.md.

Guarde esta pasta inteira num cofre de senhas, fora do banco e do Azure, e apague a copia local.
Sem o certificado, os CPFs cifrados nao podem ser lidos.
"@
    [IO.File]::WriteAllText((Join-Path $outputFullPath "LEIA-ME.txt"), $readme, [Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) {
        Get-ChildItem -LiteralPath $outputFullPath -File | ForEach-Object { & chmod 600 $_.FullName }
    }

    Write-Host "Material de chave para $Environment gerado em: $outputFullPath"
    Write-Host "Impressao digital do certificado: $($certificate.Thumbprint)"
    Write-Host "Nenhum valor foi exibido. Leia LEIA-ME.txt, guarde a pasta no cofre e depois apague-a."
}
finally {
    foreach ($buffer in @($pfx, $hashKey, $passwordBytes)) {
        if ($null -ne $buffer) {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory([byte[]]$buffer)
        }
    }
    foreach ($disposable in @($reloaded, $certificate, $rsa)) {
        if ($null -ne $disposable) {
            $disposable.Dispose()
        }
    }
}

param(
    [SecureString]$Password
)

if ($null -eq $Password) {
    $Password = Read-Host 'Enter the initial admin password' -AsSecureString
}

$password = $Password
$plainText = [System.Net.NetworkCredential]::new('', $password).Password

if ([string]::IsNullOrWhiteSpace($plainText) -or $plainText.Length -lt 12) {
    throw 'Choose an admin password with at least 12 characters.'
}

$salt = New-Object byte[] 16
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($salt)
$rng.Dispose()

$deriveBytes = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
    $plainText,
    $salt,
    100000,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$hash = $deriveBytes.GetBytes(32)
$deriveBytes.Dispose()

$value = 'pbkdf2-sha256${0}${1}${2}' -f `
    100000,
    [Convert]::ToBase64String($salt),
    [Convert]::ToBase64String($hash)

# Docker Compose treats $ specially, so the escaped value is the one to paste into .env.
Write-Output $value.Replace('$', '$$')

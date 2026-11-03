$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "Idk\Services\EmbeddedOpenRouter.g.cs"

function Write-Blob([byte[]]$blob) {
  $body = if ($blob.Length -eq 0) {
    "System.Array.Empty<byte>()"
  } else {
    $parts = foreach ($b in $blob) { "0x{0:X2}" -f $b }
    "new byte[] { " + ($parts -join ", ") + " }"
  }
  $text = @"
namespace Idk.Services;

internal static class EmbeddedOpenRouter
{
    internal const int CipherBytes = $($blob.Length);
    internal static readonly byte[] Blob = $body;
}
"@
  [System.IO.File]::WriteAllText($out, $text.Replace("`n", "`r`n"))
}

$apiKey = ""
$envPath = Join-Path $root ".env"
if (Test-Path -LiteralPath $envPath) {
  foreach ($line in [System.IO.File]::ReadAllLines($envPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
    $eq = $trimmed.IndexOf("=")
    if ($eq -lt 1) { continue }
    if ($trimmed.Substring(0, $eq).Trim() -ne "OPENROUTER_API_KEY") { continue }
    $apiKey = $trimmed.Substring($eq + 1).Trim().Trim('"').Trim("'")
  }
}

if ($apiKey.Length -lt 8) {
  Write-Blob ([byte[]]@())
  Write-Output "sealed=0"
  exit 0
}

$aesKey = [byte[]](0x7F, 0xFD, 0xFC, 0x7F, 0x78, 0x36, 0x03, 0x3C, 0x3C, 0x56, 0xB1, 0xB0, 0xE6, 0x82, 0xEC, 0xEA, 0xFB, 0x3E, 0xC0, 0x20, 0xA7, 0x78, 0xF6, 0x18, 0x57, 0xBD, 0x7A, 0xEE, 0xCE, 0x4B, 0xFF, 0x65)
$macKey = [byte[]](0x33, 0x3C, 0xDE, 0xDD, 0x61, 0x90, 0xC8, 0x32, 0x25, 0x02, 0x70, 0x51, 0xB1, 0x1C, 0xEA, 0x44, 0x87, 0x77, 0xC0, 0x00, 0x56, 0x76, 0x48, 0x13, 0xE3, 0x31, 0x60, 0x32, 0x5A, 0xAC, 0xBB, 0xA1)
$plain = [System.Text.Encoding]::UTF8.GetBytes($apiKey)
$apiKey = ""
$aes = [System.Security.Cryptography.Aes]::Create()
$aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
$aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
$aes.Key = $aesKey
$aes.GenerateIV()
$cipher = $aes.CreateEncryptor().TransformFinalBlock($plain, 0, $plain.Length)
[Array]::Clear($plain, 0, $plain.Length)
$iv = $aes.IV
$signed = New-Object byte[] ($iv.Length + $cipher.Length)
[Buffer]::BlockCopy($iv, 0, $signed, 0, $iv.Length)
[Buffer]::BlockCopy($cipher, 0, $signed, $iv.Length, $cipher.Length)
$hmac = New-Object System.Security.Cryptography.HMACSHA256 (,$macKey)
$tag = $hmac.ComputeHash($signed)
$blob = New-Object byte[] (16 + 32 + $cipher.Length)
[Buffer]::BlockCopy($iv, 0, $blob, 0, 16)
[Buffer]::BlockCopy($tag, 0, $blob, 16, 32)
[Buffer]::BlockCopy($cipher, 0, $blob, 48, $cipher.Length)
Write-Blob $blob
Write-Output ("sealed=" + $blob.Length)

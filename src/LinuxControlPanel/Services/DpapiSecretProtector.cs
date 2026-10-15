using System.Security.Cryptography;
using System.Text;
using LinuxControlPanel.Core.Storage;

namespace LinuxControlPanel.Services;

/// <summary>
/// Windows DPAPI(현재 사용자 범위)로 비밀 값을 암호화한다. 파일이 새어 나가도
/// 같은 Windows 계정이 아니면 복호화되지 않는다.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LinuxControlPanel.addressbook.v1");

    public string? Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            // 암호화할 수 없으면 저장하지 않는다(평문으로 떨어뜨리는 것보다 낫다).
            return null;
        }
    }

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedValue),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            // 다른 계정/PC 에서 만든 값이면 복호화할 수 없다. 사용자가 다시 입력하게 한다.
            return null;
        }
    }
}

namespace LinuxControlPanel.Core.Storage;

/// <summary>
/// 비밀 값을 저장 가능한 문자열로 바꾸는 장치. Windows 앱에서는 DPAPI 구현을 넣고,
/// 테스트에서는 가짜 구현을 넣어 저장 로직만 검증한다.
/// </summary>
public interface ISecretProtector
{
    /// <summary>실패하면 null 을 돌려준다(저장하지 않는다).</summary>
    string? Protect(string plainText);

    /// <summary>복호화할 수 없으면 null 을 돌려준다.</summary>
    string? Unprotect(string protectedValue);
}

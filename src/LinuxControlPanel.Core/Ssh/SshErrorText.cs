using System.Net.Sockets;
using Renci.SshNet.Common;

namespace LinuxControlPanel.Core.Ssh;

/// <summary>예외를 사용자가 고칠 수 있는 한국어 메시지로 바꾼다.</summary>
public static class SshErrorText
{
    public static string Describe(Exception exception) => exception switch
    {
        SshHostKeyRejectedException rejected => rejected.Message,
        SshAuthenticationException => "인증에 실패했습니다. 사용자 이름과 비밀번호(또는 개인키)를 확인하세요.",
        SshOperationTimeoutException => "응답 시간이 초과되었습니다. 네트워크 상태와 대상 PC 를 확인하세요.",
        SshConnectionException connection => $"SSH 연결이 끊어졌습니다: {connection.Message}",
        SocketException socket => $"대상에 연결할 수 없습니다({socket.SocketErrorCode}). 주소·포트·방화벽과 sshd 실행 여부를 확인하세요.",
        OperationCanceledException => "작업을 취소했습니다.",
        FileNotFoundException notFound => $"개인키 파일을 찾을 수 없습니다: {notFound.FileName}",
        UnauthorizedAccessException => "개인키 파일을 읽을 권한이 없습니다.",
        _ => exception.Message,
    };
}

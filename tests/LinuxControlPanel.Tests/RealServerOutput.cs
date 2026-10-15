namespace LinuxControlPanel.Tests;

/// <summary>
/// 실제 Ubuntu 20.04 서버(acerver)에서 수집 명령을 그대로 돌려 받은 출력.
/// 파서가 진짜 출력 모양에 맞는지 확인하는 기준으로 쓴다.
/// </summary>
public static class RealServerOutput
{
    public const string Raw = """
        #!HOSTNAME
        acerver
        #!UNAME
        Linux 5.4.0-216-generic
        x86_64
        #!OS
        NAME="Ubuntu"
        VERSION="20.04.6 LTS (Focal Fossa)"
        ID=ubuntu
        ID_LIKE=debian
        PRETTY_NAME="Ubuntu 20.04.6 LTS"
        VERSION_ID="20.04"
        VERSION_CODENAME=focal
        UBUNTU_CODENAME=focal
        #!CPUMODEL
        model name	: Intel(R) Pentium(R) CPU 4405U @ 2.10GHz
        #!CPUCOUNT
        4
        #!STAT1
        cpu  37957579 10927 26273617 181103302 63880 0 3580464 0 0 0
        cpu0 9545580 2338 6644778 45704879 23066 0 386220 0 0 0
        cpu1 9624997 3441 6654102 45602720 13600 0 289817 0 0 0
        cpu2 9390361 3900 6593744 45922134 13811 0 234096 0 0 0
        cpu3 9396639 1247 6380992 43873567 13401 0 2670330 0 0 0
        #!STAT2
        cpu  37957610 10927 26273633 181103370 63880 0 3580468 0 0 0
        cpu0 9545588 2338 6644782 45704897 23066 0 386221 0 0 0
        cpu1 9625004 3441 6654108 45602737 13600 0 289817 0 0 0
        cpu2 9390369 3900 6593748 45922152 13811 0 234096 0 0 0
        cpu3 9396648 1247 6380994 43873583 13401 0 2670334 0 0 0
        #!UPTIME
        647186.53 1910636.98
        #!LOADAVG
        3.51 2.26 2.43 1/13591 34068
        #!MEM
        MemTotal:       12146476 kB
        MemFree:          176920 kB
        MemAvailable:    7292208 kB
        Buffers:          749800 kB
        Cached:          6468240 kB
        SwapCached:          300 kB
        SwapTotal:       2097148 kB
        SwapFree:        2096848 kB
        #!DISK
        Filesystem                            1-blocks        Used   Available Capacity Mounted on
        /dev/mapper/ubuntu--vg-ubuntu--lv 105089261568 46139383808 53564391424      47% /
        /dev/sda2                           2040373248   119132160  1797091328       7% /boot
        #!NETDEV
        Inter-|   Receive                                                |  Transmit
         face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
        enp2s0: 78789117003 486768494    0 323194    0     0          0    190619 90620213240 696267796   22    0    0     0       0          0
        br-3a08ebcf7455:       0       0    0    0    0     0          0         0        0       0    0    0    0     0       0          0
        tailscale0: 8031802606 61665984    0    0    0     0          0         0 5540651807 62308456    0    0    0     0       0          0
            lo: 53530331711 197903845    0    0    0     0          0         0 53530331711 197903845    0    0    0     0       0          0
        docker0:  145557     918    0    0    0     0          0         0   288526     866    0    0    0     0       0          0
        #!PS
            PID %CPU %MEM COMMAND
            874 35.0  0.9 tailscaled
            711 15.5  0.2 systemd-resolve
        2456402  5.1  1.8 dotnet
            834  2.1  1.5 IssueBoardWeb.A
           1091  1.6  0.8 dockerd
        #!WHO
        #!TEMP
        57000
        51000
        55500
        59000
        #!END
        """;
}

using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Tests;

public class ServiceCollectorTests
{
    // Ubuntu 20.04 (systemd 245) 의 list-units --plain / list-unit-files 출력 형식.
    private const string ListOutput = """
        #!UNITS
        cron.service                           loaded    active   running Regular background program processing daemon
        getty@tty1.service                     loaded    active   running Getty on tty1
        networkd-dispatcher.service            loaded    active   running Dispatcher daemon for systemd-networkd
        ● plymouth-start.service               not-found inactive dead    plymouth-start.service
        snapd.seeded.service                   loaded    active   exited  Wait until snapd is fully seeded
        ssh.service                            loaded    active   running OpenBSD Secure Shell server
        apt-daily.service                      loaded    failed   failed  Daily apt download activities
        nginx.service                          loaded    activating start   A high performance web server
        #!FILES
        apache2.service                        disabled        enabled
        cron.service                           enabled         enabled
        getty@.service                         enabled         enabled
        nginx.service                          enabled         enabled
        ssh.service                            enabled         enabled
        apt-daily.service                      static          enabled
        #!END
        """;

    [Fact]
    public void ParseList_올라온_유닛과_파일만_있는_서비스를_합친다()
    {
        var services = ServiceCollector.ParseList(ListOutput);

        Assert.Equal(9, services.Count);
        Assert.Equal(services.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s => s.Name), services.Select(s => s.Name));
        Assert.DoesNotContain(services, s => s.Name == "getty@.service");

        var ssh = services.Single(s => s.Name == "ssh.service");
        Assert.Equal("ssh", ssh.ShortName);
        Assert.Equal("enabled", ssh.UnitFileState);
        Assert.Equal("OpenBSD Secure Shell server", ssh.Description);
        Assert.Equal(ServiceHealth.Running, ssh.Health);

        var apache = services.Single(s => s.Name == "apache2.service");
        Assert.Equal("disabled", apache.UnitFileState);
        Assert.Equal(ServiceHealth.Stopped, apache.Health);
    }

    [Fact]
    public void ParseList_상태별로_분류한다()
    {
        var services = ServiceCollector.ParseList(ListOutput).ToDictionary(s => s.Name);

        Assert.Equal(ServiceHealth.Active, services["snapd.seeded.service"].Health);
        Assert.Equal(ServiceHealth.Failed, services["apt-daily.service"].Health);
        Assert.Equal(ServiceHealth.Transitioning, services["nginx.service"].Health);
        Assert.Equal(ServiceHealth.Stopped, services["plymouth-start.service"].Health);
        Assert.Equal("not-found", services["plymouth-start.service"].LoadState);
    }

    [Fact]
    public void ParseList_인스턴스는_템플릿의_자동_시작_설정을_따른다()
    {
        var getty = ServiceCollector.ParseList(ListOutput).Single(s => s.Name == "getty@tty1.service");

        Assert.Equal("enabled", getty.UnitFileState);
        Assert.Equal("Getty on tty1", getty.Description);
    }

    [Fact]
    public void ParseList_systemd_가_없으면_알려준다()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ServiceCollector.ParseList("#!NOSYSTEMD\n#!UNITS\n#!FILES\n#!END\n"));

        Assert.Contains("systemd", ex.Message);
    }

    [Fact]
    public void ParseDetail_속성과_상태_출력을_읽는다()
    {
        const string raw = """
            #!SHOW
            Type=notify
            Restart=on-failure
            NRestarts=0
            MainPID=812
            MemoryCurrent=6070272
            CPUUsageNSec=1530000000
            TasksCurrent=1
            ExecStart={ path=/usr/sbin/sshd ; argv[]=/usr/sbin/sshd -D $SSHD_OPTS ; ignore_errors=no ; start_time=[n/a] ; stop_time=[n/a] ; pid=0 ; code=(null) ; status=0/0 }
            Id=ssh.service
            Description=OpenBSD Secure Shell server
            ActiveState=active
            ActiveEnterTimestamp=Mon 2026-10-05 09:12:44 KST
            #!STATUS
            ● ssh.service - OpenBSD Secure Shell server
                 Loaded: loaded (/lib/systemd/system/ssh.service; enabled; vendor preset: enabled)
                 Active: active (running) since Mon 2026-10-05 09:12:44 KST; 1 weeks 3 days ago
            #!END
            """;

        var detail = ServiceCollector.ParseDetail("ssh.service", raw);

        Assert.Equal(812, detail.MainPid);
        Assert.Equal(6070272, detail.MemoryBytes);
        Assert.Equal(1, detail.Tasks);
        Assert.Equal(TimeSpan.FromSeconds(1.53), detail.CpuTime);
        Assert.Equal("/usr/sbin/sshd -D $SSHD_OPTS", detail.ExecStartCommand);
        Assert.Equal("Mon 2026-10-05 09:12:44 KST", detail.Get("ActiveEnterTimestamp"));
        Assert.StartsWith("● ssh.service", detail.StatusText);
        Assert.Contains("active (running)", detail.StatusText);
    }

    [Fact]
    public void ParseDetail_값_없음은_null_로_둔다()
    {
        const string raw = "#!SHOW\nMemoryCurrent=[not set]\nTasksCurrent=18446744073709551615\nCPUUsageNSec=\nMainPID=0\n#!STATUS\n#!END\n";

        var detail = ServiceCollector.ParseDetail("cron.service", raw);

        Assert.Null(detail.MemoryBytes);
        Assert.Null(detail.Tasks);
        Assert.Null(detail.CpuTime);
        Assert.Equal(0, detail.MainPid);
        Assert.Equal(string.Empty, detail.StatusText);
    }

    [Theory]
    [InlineData("ssh.service", true)]
    [InlineData("getty@tty1.service", true)]
    [InlineData(@"systemd-fsck@dev-disk-by\x2duuid-1234.service", true)]
    [InlineData("ssh.service; rm -rf /", false)]
    [InlineData("a'b.service", false)]
    [InlineData("$(id).service", false)]
    [InlineData("--help", false)]
    [InlineData("", false)]
    public void IsValidUnitName_셸에_넣어도_안전한_이름만_받는다(string name, bool expected)
    {
        Assert.Equal(expected, ServiceCollector.IsValidUnitName(name));
    }

    [Fact]
    public void DetailCommand_이름을_작은따옴표로_감싼다()
    {
        var command = ServiceCollector.DetailCommand("ssh.service");

        Assert.Contains("systemctl show 'ssh.service'", command);
        Assert.Contains("systemctl status 'ssh.service'", command);
        Assert.Throws<ArgumentException>(() => ServiceCollector.DetailCommand("x;reboot"));
    }
}

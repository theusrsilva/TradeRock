namespace TradeRock.Core.Setup;

/// <summary>
/// Lê o sistema Linux a partir de uma raiz (<c>/</c> no uso real; uma árvore falsa nos testes).
/// Só leitura, sem root: /proc, /sys e /etc. Nada aqui altera o sistema.
/// </summary>
public sealed class LinuxProbe(string root = "/")
{
    private string P(string path) => Path.Combine(root, path.TrimStart('/'));

    public SystemSnapshot Read(string? userName = null)
    {
        var internet = DefaultRouteInterfaces();
        return new SystemSnapshot(
            Os: "linux",
            IsWsl: IsWsl(),
            VirtualMachine: DetectVirtualMachine(),
            Distro: ReadDistro(),
            WifiCards: ReadWifiCards(internet),
            SerialPorts: ReadSerialPorts(),
            User: ReadUser(userName ?? Environment.UserName),
            BrlttyRunning: IsProcessRunning("brltty"),
            NetworkManagerRunning: IsProcessRunning("NetworkManager"),
            LdnNetwork: ReadLdnNetworkConfig(),
            MissingTools: SystemSnapshot.WifiTools.Where(t => !HasTool(t)).ToList());
    }

    public const string NmConfPath = "/etc/NetworkManager/conf.d/zz-ldn-unmanaged.conf";
    public const string UdevLinkPath = "/etc/systemd/network/10-traderock-ldn.link";

    /// <summary>Os arquivos que o worker instala (worker/traderock_worker/radio.py) estão lá?</summary>
    public LdnNetworkConfig ReadLdnNetworkConfig() => new(
        ReadText(NmConfPath)?.Contains("interface-name:ldn*") == true,
        ReadText(UdevLinkPath)?.Contains("OriginalName=ldn*") == true);

    private static readonly string[] ToolDirs = ["/usr/local/sbin", "/usr/local/bin", "/usr/sbin", "/usr/bin", "/sbin", "/bin"];

    /// <summary>Mesmo PATH que o worker usa para os comandos do motor.</summary>
    public bool HasTool(string tool) => ToolDirs.Any(d => File.Exists(P(Path.Combine(d, tool))));

    /// <summary>WSL: /proc/version contém "microsoft" (WSL1 e WSL2).</summary>
    public bool IsWsl() =>
        ReadText("/proc/version")?.Contains("microsoft", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Máquina virtual pelo DMI (fabricante/produto) ou pela flag "hypervisor" da CPU.</summary>
    public string? DetectVirtualMachine()
    {
        var vendor = ReadText("/sys/class/dmi/id/sys_vendor")?.Trim() ?? "";
        var product = ReadText("/sys/class/dmi/id/product_name")?.Trim() ?? "";
        var known = new (string Match, string Name)[]
        {
            ("QEMU", "QEMU/KVM"), ("KVM", "KVM"), ("VMware", "VMware"), ("innotek", "VirtualBox"),
            ("VirtualBox", "VirtualBox"), ("Xen", "Xen"), ("Parallels", "Parallels"), ("Bochs", "Bochs"),
        };
        foreach (var (match, name) in known)
        {
            if (vendor.Contains(match, StringComparison.OrdinalIgnoreCase) || product.Contains(match, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && product.Contains("Virtual Machine", StringComparison.OrdinalIgnoreCase))
            return "Hyper-V";

        // Sem DMI conhecido: a flag "hypervisor" da CPU ainda indica virtualização.
        var cpuinfo = ReadText("/proc/cpuinfo");
        if (cpuinfo is not null && cpuinfo.Split('\n').Any(l => l.StartsWith("flags", StringComparison.Ordinal) && l.Split(' ').Contains("hypervisor")))
            return "desconhecida";
        return null;
    }

    public DistroInfo? ReadDistro()
    {
        var text = ReadText("/etc/os-release") ?? ReadText("/usr/lib/os-release");
        if (text is null)
            return null;
        var values = text.Split('\n')
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim().Trim('"'));
        return new DistroInfo(
            values.GetValueOrDefault("ID", ""),
            values.GetValueOrDefault("ID_LIKE", ""),
            values.GetValueOrDefault("PRETTY_NAME", values.GetValueOrDefault("NAME", "")));
    }

    /// <summary>Interfaces com rota padrão (a "internet do PC"), de /proc/net/route.</summary>
    public IReadOnlySet<string> DefaultRouteInterfaces()
    {
        var text = ReadText("/proc/net/route");
        if (text is null)
            return new HashSet<string>();
        return text.Split('\n').Skip(1)
            .Select(l => l.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.Length > 1 && c[1] == "00000000")
            .Select(c => c[0].Trim())
            .ToHashSet();
    }

    /// <summary>Cada /sys/class/ieee80211/phy*: driver, módulo, barramento, IDs e interfaces.</summary>
    public IReadOnlyList<WifiCard> ReadWifiCards(IReadOnlySet<string> internet)
    {
        var dir = P("/sys/class/ieee80211");
        if (!Directory.Exists(dir))
            return [];

        var cards = new List<WifiCard>();
        foreach (var phyDir in Directory.GetDirectories(dir).Order())
        {
            var phy = Path.GetFileName(phyDir);
            var device = Path.Combine(phyDir, "device");
            var devicePath = Resolve(device);

            var driverLink = Path.Combine(device, "driver");
            var driver = LinkName(driverLink);
            var module = LinkName(Path.Combine(driverLink, "module"));

            var interfaces = Directory.Exists(Path.Combine(device, "net"))
                ? Directory.GetDirectories(Path.Combine(device, "net")).Select(Path.GetFileName).OfType<string>().Order().ToList()
                : [];

            var bus = devicePath switch
            {
                not null when devicePath.Contains("/usb") => "usb",
                not null when devicePath.Contains("/pci") => "pci",
                not null when devicePath.Contains("mmc") => "sdio",
                _ => "other",
            };

            string? vendor = null, product = null, name = null;
            if (bus == "usb")
            {
                // O phy aponta para a interface USB (ex.: 1-1:1.0); os IDs ficam no dispositivo pai.
                var usbDevice = FindUp(devicePath, "idVendor");
                vendor = ReadText(Path.Combine(usbDevice ?? "", "idVendor"))?.Trim();
                product = ReadText(Path.Combine(usbDevice ?? "", "idProduct"))?.Trim();
                name = ReadText(Path.Combine(usbDevice ?? "", "product"))?.Trim();
            }
            else if (bus == "pci")
            {
                vendor = ReadText(Path.Combine(device, "vendor"))?.Trim().Replace("0x", "");
                product = ReadText(Path.Combine(device, "device"))?.Trim().Replace("0x", "");
            }

            cards.Add(new WifiCard(phy, interfaces, driver, module, bus, vendor, product, name,
                interfaces.Any(internet.Contains)));
        }
        return cards;
    }

    /// <summary>ttyUSB*/ttyACM* com os IDs USB (para identificar o conversor USB-serial).</summary>
    public IReadOnlyList<SerialPortInfo> ReadSerialPorts()
    {
        var dir = P("/sys/class/tty");
        if (!Directory.Exists(dir))
            return [];

        var ports = new List<SerialPortInfo>();
        foreach (var tty in Directory.GetDirectories(dir).Concat(Directory.GetFiles(dir)).Select(Path.GetFileName).OfType<string>().Order())
        {
            if (!tty.StartsWith("ttyUSB", StringComparison.Ordinal) && !tty.StartsWith("ttyACM", StringComparison.Ordinal))
                continue;
            var devicePath = Resolve(Path.Combine(dir, tty, "device"));
            var usbDevice = FindUp(devicePath, "idVendor");
            ports.Add(new SerialPortInfo(
                "/dev/" + tty,
                ReadText(Path.Combine(usbDevice ?? "", "idVendor"))?.Trim(),
                ReadText(Path.Combine(usbDevice ?? "", "idProduct"))?.Trim(),
                ReadText(Path.Combine(usbDevice ?? "", "product"))?.Trim()));
        }
        return ports;
    }

    /// <summary>
    /// Grupos do usuário em /etc/group (o que vale no próximo login) e os da sessão atual
    /// (/proc/self/status). Diferença = entrou num grupo e ainda não saiu e entrou de novo.
    /// </summary>
    public UserInfo? ReadUser(string userName)
    {
        var groupText = ReadText("/etc/group");
        var passwd = ReadText("/etc/passwd");
        if (groupText is null)
            return null;

        var groups = groupText.Split('\n')
            .Select(l => l.Split(':'))
            .Where(p => p.Length >= 4)
            .Select(p => (Name: p[0], Gid: p[2], Members: p[3].Split(',', StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

        var primaryGid = passwd?.Split('\n').Select(l => l.Split(':')).FirstOrDefault(p => p.Length > 3 && p[0] == userName)?[3];
        var member = groups.Where(g => g.Members.Contains(userName) || g.Gid == primaryGid).Select(g => g.Name).ToList();

        var sessionGids = ReadText("/proc/self/status")?.Split('\n')
            .FirstOrDefault(l => l.StartsWith("Groups:", StringComparison.Ordinal))?
            .Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToHashSet() ?? [];
        var session = groups.Where(g => sessionGids.Contains(g.Gid)).Select(g => g.Name).ToList();

        return new UserInfo(userName, member, session);
    }

    public bool IsProcessRunning(string name)
    {
        var proc = P("/proc");
        if (!Directory.Exists(proc))
            return false;
        foreach (var dir in Directory.EnumerateDirectories(proc))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _))
                continue;
            try
            {
                if (File.ReadAllText(Path.Combine(dir, "comm")).Trim() == name)
                    return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    private string? ReadText(string path)
    {
        var full = Path.IsPathRooted(path) && path.StartsWith(root, StringComparison.Ordinal) && root != "/" ? path : P(path);
        try
        {
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? LinkName(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is { } target)
                return Path.GetFileName(target.TrimEnd('/'));
            var dir = new DirectoryInfo(path);
            return dir.LinkTarget is { } t ? Path.GetFileName(t.TrimEnd('/')) : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Caminho real, resolvendo links componente por componente (como o realpath). No /sys os links
    /// são relativos e passam por outros links (phy0 → /sys/devices/…/phy0, device → ../../..),
    /// e ResolveLinkTarget sozinho resolveria em relação ao caminho digitado, não ao real.
    /// </summary>
    internal static string? Resolve(string path)
    {
        var current = Path.GetPathRoot(path) ?? "/";
        foreach (var part in path[current.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = part == ".." ? Path.GetDirectoryName(current) ?? current : Path.Combine(current, part);
            for (var hops = 0; hops < 40; hops++)
            {
                string? target;
                try
                {
                    target = new FileInfo(next).LinkTarget;
                }
                catch (IOException) { return null; }
                if (target is null)
                    break;
                next = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(next)!, target));
            }
            current = next;
        }
        return Directory.Exists(current) || File.Exists(current) ? current : null;
    }

    /// <summary>Sobe pelos diretórios até achar um que tenha <paramref name="file"/>.</summary>
    private static string? FindUp(string? start, string file)
    {
        for (var dir = start; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, file)))
                return dir;
        }
        return null;
    }
}

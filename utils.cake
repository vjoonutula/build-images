public class DockerImages
{
    public ICollection<DockerImage> Windows { get; private set; }
    public ICollection<DockerImage> Linux { get; private set; }

    public static DockerImages GetDockerImages(ICakeContext context, FilePath[] dockerfiles, string[] versions, string[] variants)
    {
        var toDockerImage = DockerImage();
        var dockerImages =
            (from version in versions
            from variant in variants
            from dockerfile in dockerfiles
            select toDockerImage(dockerfile, version, variant)).ToArray();

        return new DockerImages {
            Windows = dockerImages.Where(x => x.OS == "windows").ToArray(),
            Linux = dockerImages.Where(x => x.OS == "linux").ToArray(),
        };
    }

    private static Func<FilePath, string, string, DockerImage> DockerImage()
    {
        return (dockerFile, version, variant) => {
            var segments = dockerFile.Segments.Reverse().ToArray();
            var distro = segments[1];
            var os = segments[2];
            return new DockerImage(os: os, distro: distro, version: version, variant: variant);
        };
    }
}

public class DockerImage
{
    public string OS { get; private set; }
    public string Distro { get; private set; }
    public string Version { get; private set; }
    public string Variant { get; private set; }

    public DockerImage(string os, string distro, string version, string variant)
    {
        OS = os;
        Distro = distro;
        Version = version;
        Variant = variant;
    }

    public void Deconstruct(out string os, out string distro, out string version, out string variant)
    {
        os = OS;
        distro = Distro;
        version = Version;
        variant = Variant;
    }
}

FilePath FindToolInPath(string tool)
{
    var pathEnv = EnvironmentVariable("PATH");
    if (string.IsNullOrEmpty(pathEnv) || string.IsNullOrEmpty(tool)) return tool;

    var paths = pathEnv.Split(new []{ IsRunningOnUnix() ? ':' : ';'},  StringSplitOptions.RemoveEmptyEntries);
    return paths.Select(path => new DirectoryPath(path).CombineWithFilePath(tool)).FirstOrDefault(filePath => FileExists(filePath.FullPath));
}

void DockerStdinLogin(string username, string password)
{
    var toolPath = FindToolInPath(IsRunningOnUnix() ? "docker" : "docker.exe");
    var args = new ProcessArgumentBuilder()
        .Append("login")
        .Append("--username").AppendQuoted(username)
        .Append("--password-stdin");

    var processStartInfo = new ProcessStartInfo(toolPath.ToString(), args.Render())
    {
        RedirectStandardInput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    using (var process = new Process { StartInfo = processStartInfo })
    {
        process.Start();
        process.StandardInput.WriteLine(password);
        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception(toolPath.GetFilename() + " returned exit code " + process.ExitCode + ".");
    }
}

void DockerBuild(DockerImage dockerImage)
{
    var (os, distro, version, variant) = dockerImage;
    var workDir = DirectoryPath.FromString($"./src/{os}/{distro}");

        var tags = GetDockerTags(dockerImage);

    var buildSettings = new DockerImageBuildSettings
    {
        Rm = true,
        Tag = tags,
        File = $"{workDir}/Dockerfile",
        BuildArg = new []{ $"DOTNET_VERSION={version}", $"DOTNET_VARIANT={variant}" },
        // Pull = true,
    };

    DockerBuild(buildSettings, workDir.ToString());
}

void DockerPush(DockerImage dockerImage)
{
    var tags = GetDockerTags(dockerImage);

    foreach (var tag in tags)
    {
        DockerPush(tag);
    }
}

string[] GetDockerTags(DockerImage dockerImage) {
    var name = $"gittools/build-images";
    var (os, distro, version, variant) = dockerImage;

    var tags = new List<string> {
        $"{name}:{distro}-{variant}-{version}",
    };

    if (version == "2.2") {
        tags.AddRange(new[] {
            $"{name}:{distro}-{variant}-latest",
        });
    }

    return tags.ToArray();
}
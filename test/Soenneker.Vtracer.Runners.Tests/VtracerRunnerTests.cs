using Soenneker.Vtracer.Runners.Utils.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Vtracer.Runners.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class VtracerRunnerTests : HostedUnitTest
{
    private readonly IFileOperationsUtil _runner;

    public VtracerRunnerTests(Host host) : base(host)
    {
        _runner = Resolve<IFileOperationsUtil>(true);
    }

    [Test]
    public void Default()
    {

    }
}

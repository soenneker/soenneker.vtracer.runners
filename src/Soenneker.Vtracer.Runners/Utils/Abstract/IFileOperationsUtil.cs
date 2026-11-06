using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Vtracer.Runners.Utils.Abstract;

/// <summary>Prepares both x64 CLI distributions from the same latest GitHub release.</summary>
public interface IFileOperationsUtil
{
    /// <summary>Writes executables, the upstream license, and provenance beneath each runtime identifier.</summary>
    /// <param name="outputDirectory">An empty destination directory owned by the caller.</param>
    /// <param name="cancellationToken">Cancels downloads and file operations.</param>
    ValueTask Process(string outputDirectory, CancellationToken cancellationToken = default);
}

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Serval.EnvironmentOracle;

internal enum OracleExitCode
{
    Success = 0,
    Mismatch = 20,
    MalformedManifest = 21,
    MissingCredential = 22,
    DuplicateName = 23,
    TrailingData = 24,
    OverLimit = 25,
    UnexpectedFailure = 26,
}

internal static class OracleApplication
{
    internal static OracleExitCode Run(
        string[] args,
        string? credentialDirectory,
        Func<string, string?> environmentLookup)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environmentLookup);

        if (args.Length != 0)
        {
            return OracleExitCode.UnexpectedFailure;
        }

        if (string.IsNullOrEmpty(credentialDirectory) || !Path.IsPathFullyQualified(credentialDirectory))
        {
            return OracleExitCode.MissingCredential;
        }

        try
        {
            var credentialPath = Path.Combine(credentialDirectory, ExpectationManifest.CredentialName);
            using var source = new FileStream(
                credentialPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var manifest = ExpectationManifest.Read(source);
            return Matches(manifest, environmentLookup)
                ? OracleExitCode.Success
                : OracleExitCode.Mismatch;
        }
        catch (ExpectationManifestException exception)
        {
            return exception.Error switch
            {
                ExpectationManifestError.DuplicateName => OracleExitCode.DuplicateName,
                ExpectationManifestError.TrailingData => OracleExitCode.TrailingData,
                ExpectationManifestError.OverLimit => OracleExitCode.OverLimit,
                _ => OracleExitCode.MalformedManifest,
            };
        }
        catch (FileNotFoundException)
        {
            return OracleExitCode.MissingCredential;
        }
        catch (DirectoryNotFoundException)
        {
            return OracleExitCode.MissingCredential;
        }
        catch (UnauthorizedAccessException)
        {
            return OracleExitCode.MissingCredential;
        }
        catch (IOException)
        {
            return OracleExitCode.UnexpectedFailure;
        }
        catch
        {
            return OracleExitCode.UnexpectedFailure;
        }
    }

    private static bool Matches(ExpectationManifest manifest, Func<string, string?> environmentLookup)
    {
        var encoding = new UTF8Encoding(false, true);
        foreach (var entry in manifest.Entries)
        {
            var observed = environmentLookup(entry.Name);
            if (!entry.IsPresent)
            {
                if (observed is not null)
                {
                    return false;
                }

                continue;
            }

            if (observed is null)
            {
                return false;
            }

            var byteCount = encoding.GetByteCount(observed);
            var observedBytes = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
            try
            {
                var written = encoding.GetBytes(observed, observedBytes);
                if (!CryptographicOperations.FixedTimeEquals(
                    observedBytes.AsSpan(0, written),
                    manifest.GetValueBytes(entry)))
                {
                    return false;
                }
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(observedBytes.AsSpan(0, byteCount));
                ArrayPool<byte>.Shared.Return(observedBytes);
            }
        }

        return true;
    }
}

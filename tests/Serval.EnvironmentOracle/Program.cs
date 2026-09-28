using Serval.EnvironmentOracle;

return (int)OracleApplication.Run(
    args,
    Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY"),
    Environment.GetEnvironmentVariable);

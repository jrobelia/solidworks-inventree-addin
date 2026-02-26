namespace SwInventreeAddin.Config
{
    public interface IConfigProvider
    {
        ServerConfig? GetServerConfig();
        void SaveServerConfig(ServerConfig config);
    }
}

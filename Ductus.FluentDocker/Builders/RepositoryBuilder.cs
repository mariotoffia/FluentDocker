using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
    public sealed class RepositoryBuilder
    {
        private string _name;
        private string _user;
        private string _password;
        public RepositoryBuilder(string name = null, string user = null, string pass = null)
        {
            _name = name;
            _user = user;
            _password = pass;
        }

        public RepositoryBuilder User(string user)
        {
            _user = user;
            return this;
        }
        public RepositoryBuilder Password(string password)
        {
            _password = password;
            return this;
        }

        public RepositoryBuilder Build(IHostService host)
        {
            host?.Host.Login(_user, _password);
            return this;
        }
    }
}
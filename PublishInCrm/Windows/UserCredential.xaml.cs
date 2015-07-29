using System;
using System.Reflection;
using System.Windows;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;

namespace CemYabansu.PublishInCrm
{
    public partial class UserCredential
    {
        public string ConnectionString { get; set; }

        public UserCredential()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var server = ServerTextBox.Text;
            var domain = DomainTextBox.Text;
            var username = UsernameTextBox.Text;
            var password = PasswordTextBox.Password;

            ConnectionStatusLabel.Dispatcher.BeginInvoke(new Action(() => ConnectionStatusLabel.Content = "Connecting..."));

            TestConnection(server, domain, username, password);
        }

        public void TestConnection(string server, string domain, string username, string password)
        {
            var connectionString = string.Format("Server={0}; Domain={1}; Username={2}; Password={3}",
                                                    server, domain, username, password);
            TestConnection(connectionString);
        }

        public void TestConnection(string connectionString)
        {
            try
            {
                var crmConnection = CrmConnection.Parse(connectionString);
                //to escape "another assembly" exception
                crmConnection.ProxyTypesAssembly = Assembly.GetExecutingAssembly();
                OrganizationService orgService;
                using (orgService = new OrganizationService(crmConnection))
                {
                    orgService.Execute(new WhoAmIRequest());
                    ConnectionString = connectionString;
                    Close();
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusLabel.Dispatcher.BeginInvoke(new Action(() => ConnectionStatusLabel.Content = "Connection Failed."));
            }
        }


    }
}

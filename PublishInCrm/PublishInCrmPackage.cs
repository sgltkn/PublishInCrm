using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using CemYabansu.PublishInCrm.Windows;
using EnvDTE;
using EnvDTE80;
using Microsoft.Crm.Sdk.Messages;

using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using OutputWindow = CemYabansu.PublishInCrm.Windows.OutputWindow;
using Thread = System.Threading.Thread;

namespace CemYabansu.PublishInCrm
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(GuidList.guidPublishInCrmPkgString)]
    public sealed class PublishInCrmPackage : Package
    {
        private readonly string[] _expectedExtensions = { ".js", ".htm", ".html", ".css", ".png", ".jpg", ".jpeg", ".gif", ".xml" };

        private OutputWindow _outputWindow;

        public PublishInCrmPackage()
        {
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this));
            base.Initialize();

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                // Create the command for the publish in crm.
                CommandID publishInCrmCommandID = new CommandID(GuidList.guidPublishInCrmCmdSet, (int)PkgCmdIDList.cmdidPublishInCrm);
                MenuCommand publishInCrmMenuItem = new MenuCommand(PublishInCrmCallback, publishInCrmCommandID);
                mcs.AddCommand(publishInCrmMenuItem);

                // Create the command for the publish in crm(solution explorer).
                CommandID publishInCrmMultipleCommandID = new CommandID(GuidList.guidPublishInCrmCmdSet, (int)PkgCmdIDList.cmdidPublishInCrmMultiple);
                MenuCommand publishInCrmMultipleMenuItem = new MenuCommand(PublishInCrmMultipleCallback, publishInCrmMultipleCommandID);
                mcs.AddCommand(publishInCrmMultipleMenuItem);

            }
        }

        private void PublishInCrmMultipleCallback(object sender, EventArgs e)
        {
            PublishInCrm(true);
        }

        private List<string> GetSelectedFilesPath(bool isFromSolutionExplorer)
        {
            var dte = (DTE2)GetService(typeof(SDTE));
            var selectedItems = dte.SelectedItems;

            if (isFromSolutionExplorer)
            {
                List<string> list = new List<string>();
                foreach (SelectedItem selItem in selectedItems)
                {
                    list.Add(selItem.ProjectItem.FileNames[0]);
                }
                return list;
            }

            return new List<string> { dte.ActiveDocument.FullName };
        }

        private void PublishInCrm(bool isFromSolutionExplorer)
        {
            _outputWindow = new OutputWindow();
            _outputWindow.Show();

            //getting selected files
            List<string> selectedFiles = GetSelectedFilesPath(isFromSolutionExplorer);

            //checking selected files extensions 
            var inValidFiles = CheckFilesExtension(selectedFiles);
            if (inValidFiles.Count > 0)
            {
                AddErrorLineToOutputWindow(string.Format("Error : Invalid file extensions : \n\t- {0}", string.Join("\n\t- ", inValidFiles)));
                return;
            }

            //getting connection string
            var solutionPath = GetSolutionPath();
            var connectionString = GetConnectionString(solutionPath);
            if (connectionString == string.Empty)
            {
                AddErrorLineToOutputWindow("Error : Connection string is not found.");

                var userCredential = new UserCredential();
                userCredential.ShowDialog();

                if (string.IsNullOrEmpty(userCredential.ConnectionString))
                {
                    AddErrorLineToOutputWindow("Error : Connection failed.");
                    return;
                }

                connectionString = userCredential.ConnectionString;
                WriteConnectionStringToFile(Path.GetFileNameWithoutExtension(solutionPath), connectionString, Path.GetDirectoryName(solutionPath));
            }

            //updating/creating files one by one
            var thread = new Thread(o => UpdateWebResources(connectionString, selectedFiles));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void UpdateWebResources(string connectionString, List<string> selectedFiles)
        {
            try
            {
                List<WebResource> toBePublishedWebResources = new List<WebResource>();
                OrganizationService orgService;
                var crmConnection = CrmConnection.Parse(connectionString);
                //to escape "another assembly" exception
                crmConnection.ProxyTypesAssembly = Assembly.GetExecutingAssembly();
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                using (orgService = new OrganizationService(crmConnection))
                {
                    AddLineToOutputWindow("Connected to : " + crmConnection.ServiceUri);
                    for (int i = 0; i < selectedFiles.Count; i++)
                    {
                        var fileName = Path.GetFileName(selectedFiles[i]);
                        var choosenWebresource = GetWebresource(orgService, fileName);

                        if (choosenWebresource == null)
                        {
                            AddErrorLineToOutputWindow(string.Format("Error : {0} is not exist in CRM.",fileName));
                            AddLineToOutputWindow("Creating new webresource..");

                            choosenWebresource = CreateWebResource(fileName, orgService, selectedFiles[i]);

                            if (choosenWebresource == null)
                            {
                                AddLineToOutputWindow("Creating new webresource is cancelled.");
                                continue;
                            }

                            AddLineToOutputWindow(string.Format("{0} is created.", fileName));
                            
                        }
                        else
                        {
                            AddLineToOutputWindow("Updating to Webresource..");
                            UpdateWebResource(orgService,choosenWebresource,selectedFiles[i]);
                            AddLineToOutputWindow(string.Format("{0} is updated.", choosenWebresource.Name));
                        }
                        toBePublishedWebResources.Add(choosenWebresource);
                    }

                    if (toBePublishedWebResources.Count < 1)
                    {
                        AddLineToOutputWindow("There is no webresource to publish.");
                        return;
                    }

                    //publishing webresources
                    AddLineToOutputWindow("Publishing the webresource..");
                    PublishWebResources(orgService,toBePublishedWebResources);

                    var webResourcesNames = new string[toBePublishedWebResources.Count];
                    for (var i = 0; i < toBePublishedWebResources.Count; i++)
                    {
                        webResourcesNames[i] = toBePublishedWebResources[i].Name;
                    }
                    AddLineToOutputWindow(string.Format("{0} published.", string.Join(", ", webResourcesNames)));
                }
                stopwatch.Stop();
                AddLineToOutputWindow(string.Format("Time : {0}",stopwatch.Elapsed));
            }
            catch (Exception ex)
            {
                AddErrorLineToOutputWindow("Error : " + ex.Message);
            }
        }

        private void PublishWebResources(OrganizationService orgService, List<WebResource> toBePublishedWebResources)
        {
            var webResourcesString = "";
            foreach (var webResource in toBePublishedWebResources)
                webResourcesString = webResourcesString + string.Format("<webresource>{0}</webresource>", webResource.Id);

            var prequest = new PublishXmlRequest
            {
                ParameterXml = string.Format("<importexportxml><webresources>{0}</webresources></importexportxml>", webResourcesString)
            };
            orgService.Execute(prequest);
        }

        private void UpdateWebResource(OrganizationService orgService, WebResource choosenWebresource, string selectedFile)
        {
            choosenWebresource.Content = GetEncodedFileContents(selectedFile);
            var updateRequest = new UpdateRequest
            {
                Target = choosenWebresource
            };
            orgService.Execute(updateRequest);
        }

        private WebResource CreateWebResource(string fileName, OrganizationService orgService , string filePath)
        {
            var createWebresoruce = new CreateWebResourceWindow(fileName);
            createWebresoruce.ShowDialog();

            if (createWebresoruce.CreatedWebResource == null) 
                return null;

            var createdWebresource = createWebresoruce.CreatedWebResource;
            createdWebresource.Content = GetEncodedFileContents(filePath);
            createdWebresource.Id = orgService.Create(createdWebresource);
            return createdWebresource;
        }

        private List<string> CheckFilesExtension(List<string> selectedFilePath)
        {
            var invalidFiles = new List<string>();
            for (var i = 0; i < selectedFilePath.Count; i++)
            {
                var selectedFileExtension = Path.GetExtension(selectedFilePath[i]);
                if (_expectedExtensions.All(t => t != selectedFileExtension))
                    invalidFiles.Add(Path.GetFileName(selectedFilePath[i]));
            }
            return invalidFiles;
        }

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// </summary>
        private Stopwatch _myStopwatch;
        private void PublishInCrmCallback(object sender, EventArgs e)
        {
            _myStopwatch = new Stopwatch();
            _myStopwatch.Start();

            _outputWindow = new OutputWindow();
            _outputWindow.Show();

            var selectedFilePath = GetSelectedFilePath();
            if (!CheckFileExtension(selectedFilePath))
            {
                AddErrorLineToOutputWindow("Error : Selected file extension is not valid.");
                return;
            }

            var solutionPath = GetSolutionPath();
            var connectionString = GetConnectionString(solutionPath);
            if (connectionString == string.Empty)
            {
                AddErrorLineToOutputWindow("Error : Connection string is not found.");

                var userCredential = new UserCredential();
                userCredential.ShowDialog();

                if (string.IsNullOrEmpty(userCredential.ConnectionString))
                {
                    AddErrorLineToOutputWindow("Error : Connection failed.");
                    return;
                }

                connectionString = userCredential.ConnectionString;

                WriteConnectionStringToFile(Path.GetFileNameWithoutExtension(solutionPath), connectionString, Path.GetDirectoryName(solutionPath));

            }

            AddLineToOutputWindow(string.Format("Publishig the {0} in Crm..", Path.GetFileName(selectedFilePath)));

            //Start the thread for updating and publishing the webresource.
            var thread =
                new Thread(o => UpdateTheWebresource(selectedFilePath, connectionString));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void UpdateTheWebresource(string selectedFilePath, string connectionString)
        {
            try
            {
                OrganizationService orgService;
                var crmConnection = CrmConnection.Parse(connectionString);
                //to escape "another assembly" exception
                crmConnection.ProxyTypesAssembly = Assembly.GetExecutingAssembly();
                using (orgService = new OrganizationService(crmConnection))
                {
                    var isCreateRequest = false;
                    var fileName = Path.GetFileName(selectedFilePath);
                    var choosenWebresource = GetWebresource(orgService, fileName);

                    
                    if (choosenWebresource == null)
                    {
                        AddErrorLineToOutputWindow("Error : Selected file is not exist in CRM.");
                        AddLineToOutputWindow("Creating new webresource..");

                        var createWebresoruce = new CreateWebResourceWindow(fileName);
                        createWebresoruce.ShowDialog();

                        if (createWebresoruce.CreatedWebResource == null)
                        {
                            AddLineToOutputWindow("Creating new webresource is cancelled.");
                            return;
                        }

                        isCreateRequest = true;
                        choosenWebresource = createWebresoruce.CreatedWebResource;
                    }

                    choosenWebresource.Content = GetEncodedFileContents(selectedFilePath);

                    if (isCreateRequest)
                    {
                        //create function returns, created webresource's guid.
                        choosenWebresource.Id = orgService.Create(choosenWebresource);
                        AddLineToOutputWindow("Webresource is created.");
                    }
                    else
                    {
                        AddLineToOutputWindow("Updating to Webresource..");
                        var updateRequest = new UpdateRequest
                        {
                            Target = choosenWebresource
                        };
                        orgService.Execute(updateRequest);
                        AddLineToOutputWindow("Webresource is updated.");
                    }

                    AddLineToOutputWindow("Publishing the webresource..");
                    var prequest = new PublishXmlRequest
                    {
                        ParameterXml = string.Format("<importexportxml><webresources><webresource>{0}</webresource></webresources></importexportxml>", choosenWebresource.Id)
                    };
                    orgService.Execute(prequest);
                    AddLineToOutputWindow("Webresource is published.");
                }
                _myStopwatch.Stop();
                AddLineToOutputWindow(string.Format("Time : " + _myStopwatch.Elapsed));
            }
            catch (Exception ex)
            {
                AddErrorLineToOutputWindow("Error : " + ex.Message);
            }
        }

        private WebResource GetWebresource(OrganizationService orgService, string filename)
        {
            var webresourceResult = WebresourceResult(orgService, filename);

            if (webresourceResult.Entities.Count == 0)
            {
                filename = Path.GetFileNameWithoutExtension(filename);
                webresourceResult = WebresourceResult(orgService, filename);
                if (webresourceResult.Entities.Count == 0)
                {
                    return null;
                }
            }

            return new WebResource()
            {
                Name = webresourceResult[0].GetAttributeValue<string>("name"),
                DisplayName = webresourceResult[0].GetAttributeValue<string>("displayname"),
                Id = webresourceResult[0].GetAttributeValue<Guid>("webresourceid")
            };
        }

        private static EntityCollection WebresourceResult(OrganizationService orgService, string filename)
        {
            string fetchXml = string.Format(@"<fetch mapping='logical' version='1.0' >
                            <entity name='webresource' >
                                <attribute name='webresourceid' />
                                <attribute name='name' />
                                <attribute name='displayname' />
                                <filter type='and' >
                                    <condition attribute='name' operator='eq' value='{0}' />
                                </filter>
                            </entity>
                        </fetch>", filename);

            QueryBase query = new FetchExpression(fetchXml);

            var webresourceResult = orgService.RetrieveMultiple(query);
            return webresourceResult;
        }

        public string GetEncodedFileContents(string pathToFile)
        {
            var fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read);
            byte[] binaryData = new byte[fs.Length];
            fs.Read(binaryData, 0, (int)fs.Length);
            fs.Close();
            return Convert.ToBase64String(binaryData, 0, binaryData.Length);
        }

        /// <summary>
        /// This function reads the projectPath\credential.xml file.
        /// Gets the connection string and return it. If it doesn't exist, returns String.Empty
        /// </summary>
        /// <param name="projectPath">Path of project file.</param>
        private string GetConnectionString(string projectPath)
        {
            if (Path.HasExtension(projectPath))
                projectPath = Path.GetDirectoryName(projectPath);

            var filePath = projectPath + "\\credential.xml";

            while (!File.Exists(filePath))
            {
                projectPath = Directory.GetParent(projectPath).FullName;
                if (projectPath == Path.GetPathRoot(projectPath)) return string.Empty;
                filePath = projectPath + "\\credential.xml";
            }

            var reader = new StreamReader
                (
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read)
                );
            var doc = new XmlDocument();
            var xmlIn = reader.ReadToEnd();
            reader.Close();

            try
            {
                doc.LoadXml(xmlIn);
            }
            catch (XmlException)
            {
                return string.Empty;
            }

            var nodes = doc.GetElementsByTagName("string");
            foreach (XmlNode value in nodes)
            {
                var reStr = value.ChildNodes[0].Value;
                return reStr;
            }
            return string.Empty;
        }

        private string GetSelectedFilePath()
        {
            var dte = (DTE2)GetService(typeof(SDTE));
            return dte.ActiveDocument.FullName;
        }

        private string GetSolutionPath()
        {
            var dte = (DTE2)GetService(typeof(SDTE));
            return dte.Solution.FullName;
        }

        private void WriteConnectionStringToFile(string projectName, string connectionString, string path)
        {
            var xmlDoc = new XmlDocument();
            var rootNode = xmlDoc.CreateElement("connectionString");
            xmlDoc.AppendChild(rootNode);

            var nameNode = xmlDoc.CreateElement("name");
            nameNode.InnerText = projectName;
            rootNode.AppendChild(nameNode);

            var connectionStringNode = xmlDoc.CreateElement("string");
            connectionStringNode.InnerText = connectionString;
            rootNode.AppendChild(connectionStringNode);

            xmlDoc.Save(path + "\\credential.xml");
        }

        private bool CheckFileExtension(string selectedFilePath)
        {
            var selectedFileExtension = Path.GetExtension(selectedFilePath);
            return _expectedExtensions.Any(t => t == selectedFileExtension);
        }

        private void AddLineToOutputWindow(string text)
        {
            _outputWindow.AddLineToTextBox(text);
        }

        private void AddErrorLineToOutputWindow(string errorMessage)
        {
            _outputWindow.AddErrorLineToTextBox(errorMessage);
        }


    }
}

﻿using EFCorePowerTools;
using EFCorePowerTools.Extensions;
using EnvDTE;
using EnvDTE80;
using ErikEJ.SqlCeScripting;
using Microsoft.VisualStudio.Data.Core;
using Microsoft.VisualStudio.Data.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using VSLangProj;

// ReSharper disable once CheckNamespace
namespace ErikEJ.SqlCeToolbox.Helpers
{
    internal class EnvDteHelper
    {
        internal static Dictionary<string, DatabaseInfo> GetDataConnections(EFCorePowerToolsPackage package,
    bool includeServerConnections = true)
        {
            // http://www.mztools.com/articles/2007/MZ2007018.aspx
            Dictionary<string, DatabaseInfo> databaseList = new Dictionary<string, DatabaseInfo>();
            var dataExplorerConnectionManager = package.GetService<IVsDataExplorerConnectionManager>();
            Guid provider40 = new Guid(Resources.SqlCompact40Provider);
            Guid provider40Private = new Guid(Resources.SqlCompact40PrivateProvider);
            Guid providerSqLite = new Guid(Resources.SQLiteProvider);
            Guid providerSqlitePrivate = new Guid(Resources.SqlitePrivateProvider);

            bool isV40Installed = RepositoryHelper.IsV40Installed() &&
                (DdexProviderIsInstalled(provider40) || DdexProviderIsInstalled(provider40Private));
            if (dataExplorerConnectionManager != null)
            {
                foreach (var connection in dataExplorerConnectionManager.Connections.Values)
                {
                    try
                    {
                        var objProviderGuid = connection.Provider;
                        if (objProviderGuid == provider40 && isV40Installed ||
                            objProviderGuid == provider40Private && isV40Installed)
                        {
                            DatabaseType dbType = DatabaseType.SQLCE40;
                            var serverVersion = "4.0";

                            var sConnectionString =
                                DataProtection.DecryptString(connection.EncryptedConnectionString);
                            if (!sConnectionString.Contains("Mobile Device"))
                            {
                                DatabaseInfo info = new DatabaseInfo()
                                {
                                    Caption = connection.DisplayName,
                                    FromServerExplorer = true,
                                    DatabaseType = dbType,
                                    ServerVersion = serverVersion,
                                    ConnectionString = sConnectionString
                                };
                                info.FileIsMissing = RepositoryHelper.IsMissing(info);
                                if (!databaseList.ContainsKey(sConnectionString))
                                    databaseList.Add(sConnectionString, info);
                            }
                        }

                        if (objProviderGuid == providerSqLite
                            || objProviderGuid == providerSqlitePrivate)
                        {
                            DatabaseType dbType = DatabaseType.SQLite;

                            var sConnectionString =
                                DataProtection.DecryptString(connection.EncryptedConnectionString);
                            DatabaseInfo info = new DatabaseInfo()
                            {
                                Caption = connection.DisplayName,
                                FromServerExplorer = true,
                                DatabaseType = dbType,
                                ServerVersion = RepositoryHelper.SqliteEngineVersion,
                                ConnectionString = sConnectionString
                            };
                            info.FileIsMissing = RepositoryHelper.IsMissing(info);
                            if (!databaseList.ContainsKey(sConnectionString))
                                databaseList.Add(sConnectionString, info);
                        }
                        if (includeServerConnections && objProviderGuid == new Guid(Resources.SqlServerDotNetProvider))
                        {
                            var sConnectionString = DataProtection.DecryptString(connection.EncryptedConnectionString);
                            var info = new DatabaseInfo()
                            {
                                Caption = connection.DisplayName,
                                FromServerExplorer = true,
                                DatabaseType = DatabaseType.SQLServer,
                                ServerVersion = string.Empty,
                                ConnectionString = sConnectionString
                            };
                            if (!databaseList.ContainsKey(sConnectionString))
                                databaseList.Add(sConnectionString, info);
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                    catch (NullReferenceException)
                    {
                    }
                }
            }
            return databaseList;
        }

        internal static bool DdexProviderIsInstalled(Guid id)
        {
            try
            {
                var objIVsDataProviderManager =
                    Package.GetGlobalService(typeof(IVsDataProviderManager)) as IVsDataProviderManager;
                return objIVsDataProviderManager != null &&
                    objIVsDataProviderManager.Providers.TryGetValue(id, out IVsDataProvider _);
            }
            catch
            {
                //Ignored
            }
            return false;
        }

        internal static DatabaseInfo PromptForInfo(EFCorePowerToolsPackage package)
        {
            // Show dialog with SqlClient selected by default
            var dialogFactory = package.GetService<IVsDataConnectionDialogFactory>();
            var dialog = dialogFactory.CreateConnectionDialog();
            dialog.AddAllSources();
            dialog.SelectedSource = new Guid("067ea0d9-ba62-43f7-9106-34930c60c528");
            var dialogResult = dialog.ShowDialog(connect: true);

            if (dialogResult == null) return new DatabaseInfo {DatabaseType = DatabaseType.SQLCE35};

            var info = GetDatabaseInfo(package, dialogResult.Provider, DataProtection.DecryptString(dialog.EncryptedConnectionString));
            SaveDataConnection(package, dialog.EncryptedConnectionString, info.DatabaseType, new Guid(info.Size));
            return info;
        }

        internal static void SaveDataConnection(EFCorePowerToolsPackage package, string encryptedConnectionString,
            DatabaseType dbType, Guid provider)
        {
            var dataExplorerConnectionManager = package.GetService<IVsDataExplorerConnectionManager>();
            var savedName = GetFileName(DataProtection.DecryptString(encryptedConnectionString), dbType);
            dataExplorerConnectionManager.AddConnection(savedName, provider, encryptedConnectionString, true);
        }

        private static DatabaseInfo GetDatabaseInfo(EFCorePowerToolsPackage package, Guid provider, string connectionString)
        {
            var dbType = DatabaseType.SQLCE35;
            var providerInvariant = "N/A";
            var providerGuid = Guid.Empty.ToString();
            // Find provider
            var providerManager = package.GetService<IVsDataProviderManager>();
            IVsDataProvider dp;
            providerManager.Providers.TryGetValue(provider, out dp);
            if (dp != null)
            {
                providerInvariant = (string)dp.GetProperty("InvariantName");
                dbType = DatabaseType.SQLCE35;
                if (providerInvariant == "System.Data.SqlServerCe.4.0")
                {
                    dbType = DatabaseType.SQLCE40;
                    providerGuid = EFCorePowerTools.Resources.SqlCompact40PrivateProvider;
                }
                if (providerInvariant == "System.Data.SQLite.EF6")
                {
                    dbType = DatabaseType.SQLite;
                    providerGuid = EFCorePowerTools.Resources.SqlitePrivateProvider;
                }
                if (providerInvariant == "System.Data.SqlClient")
                {
                    dbType = DatabaseType.SQLServer;
                    providerGuid = EFCorePowerTools.Resources.SqlServerDotNetProvider;
                }
            }
            return new DatabaseInfo
            {
                DatabaseType = dbType,
                ConnectionString = connectionString,
                ServerVersion = providerInvariant,
                Size = providerGuid
            };
        }

        private static string GetFilePath(string connectionString, DatabaseType dbType)
        {
            var helper = RepositoryHelper.CreateEngineHelper(dbType);
            return helper.PathFromConnectionString(connectionString);
        }

        private static string GetFileName(string connectionString, DatabaseType dbType)
        {
            if (dbType == DatabaseType.SQLServer)
            {
                var helper = new SqlServerHelper();
                return helper.PathFromConnectionString(connectionString);
            }
            var filePath = GetFilePath(connectionString, dbType);
            return Path.GetFileName(filePath);
        }

        internal static bool IsSqLiteDbProviderInstalled()
        {
            try
            {
                System.Data.Common.DbProviderFactories.GetFactory("System.Data.SQLite.EF6");
            }
            catch
            {
                return false;
            }
            return true;
        }

        public Tuple<bool, string> ContainsEfCoreReference(Project project, DatabaseType dbType)
        {
            var providerPackage = "Microsoft.EntityFrameworkCore.SqlServer";
            if (dbType == DatabaseType.SQLCE40)
            {
                providerPackage = "EntityFrameworkCore.SqlServerCompact40";
            }
            if (dbType == DatabaseType.SQLite)
            {
                providerPackage = "Microsoft.EntityFrameworkCore.Sqlite";
            }

            var vsProject = project.Object as VSProject;
            if (vsProject == null) return new Tuple<bool, string>(false, providerPackage);
            for (var i = 1; i < vsProject.References.Count + 1; i++)
            {
                if (vsProject.References.Item(i).Name.Equals(providerPackage))
                {
                    return new Tuple<bool, string>(true, providerPackage);
                }
            }
            return new Tuple<bool, string>(false, providerPackage);
        }


        public Dictionary<string, string> GetDacpacFilesInActiveSolution(DTE dte)
        {
            var result = new Dictionary<string, string>();

            if (!dte.Solution.IsOpen)
            {
                result.Add("No open Solution", null);
                return result;
            }

            string path = null;
            TryGetInitialPath(dte, out path);

            if (path != null)
            {
                //TODO Implement when project inspection works
                var files = DirSearch(path, "*.sqlproj");
                foreach (string file in files)
                {
                    var key = Path.GetFileNameWithoutExtension(file);
                    result.Add(key, file);
                }

                files = DirSearch(path, "*.dacpac");
                foreach (string file in files)
                {
                    var key = file;
                    if (key.Length > 55)
                    {
                        key = "..." + key.Substring(key.Length - 55);
                    }
                    result.Add(key, file);
                }
            }

            if (result.Count == 0)
                result.Add("No .dacpac files found in Solution folders", null);

            return result;
        }

        private static List<string> DirSearch(string sDir, string pattern)
        {
            var files = new List<String>();

            try
            {
                foreach (string f in Directory.GetFiles(sDir, pattern))
                {
                    files.Add(f);
                }
                foreach (string d in Directory.GetDirectories(sDir))
                {
                    files.AddRange(DirSearch(d, pattern));
                }
            }
            catch
            {
            }

            return files;
        }

        private bool TryGetInitialPath(DTE dte, out string path)
        {
            var dteHelper = new EnvDteHelper();
            try
            {
                path = GetInitialFolder(dte);
                return true;
            }
            catch
            {
                path = null;
                return false;
            }
        }

        public string GetInitialFolder(DTE dte)
        {
            if (!dte.Solution.IsOpen)
                return null;
            return Path.GetDirectoryName(dte.Solution.FullName);
        }

        internal static string BuildSqlProj(DTE dte, string sqlprojPath)
        {
            if (sqlprojPath.EndsWith(".dacpac")) return sqlprojPath;

            var project = GetProject(dte, sqlprojPath);
            if (project == null) return null;

            var files = DirSearch(Path.GetDirectoryName(project.FullName), "*.dacpac");
            foreach (var file in files)
            {
                File.Delete(file);
            }

            if (!project.TryBuild()) return null;

            files = DirSearch(Path.GetDirectoryName(project.FullName), "*.dacpac");
            foreach (var file in files)
            {
                if (File.GetLastWriteTime(file) > DateTime.Now.AddSeconds(-1))
                {
                    return file;
                }
            }

            return null;
        }

        private static Project GetProject(DTE dte, string projectItemPath)
        {
            var projects = Projects(dte);
            foreach (var project in projects)
            {
                if (project.FullName == projectItemPath)
                {
                    return project;
                }
            }
            return null;
        }

        public static IList<Project> Projects(DTE dte)
        {
            Projects projects = dte.Solution.Projects;
            List<Project> list = new List<Project>();
            var item = projects.GetEnumerator();
            while (item.MoveNext())
            {
                var project = item.Current as Project;
                if (project == null)
                {
                    continue;
                }

                if (project.Kind == "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")
                {
                    list.AddRange(GetSolutionFolderProjects(project));
                }
                else
                {
                    list.Add(project);
                }
            }

            return list;
        }

        private static IEnumerable<Project> GetSolutionFolderProjects(Project solutionFolder)
        {
            List<Project> list = new List<Project>();
            for (var i = 1; i <= solutionFolder.ProjectItems.Count; i++)
            {
                var subProject = solutionFolder.ProjectItems.Item(i).SubProject;
                if (subProject == null)
                {
                    continue;
                }

                // If this is another solution folder, do a recursive call, otherwise add
                if (subProject.Kind == "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")
                {
                    list.AddRange(GetSolutionFolderProjects(subProject));
                }
                else
                {
                    list.Add(subProject);
                }
            }
            return list;
        }
    
        // <summary>
        //     Helper method to show an error message within the shell.  This should be used
        //     instead of MessageBox.Show();
        // </summary>
        // <param name="errorText">Text to display.</param>
        public static void ShowError(string errorText)
        {
            ShowMessageBox(
                errorText, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                OLEMSGICON.OLEMSGICON_CRITICAL);
        }

        public static DialogResult ShowMessage(string messageText)
        {
            return ShowMessageBox(messageText, null, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST, OLEMSGICON.OLEMSGICON_INFO);
        }

        // <summary>
        //     Helper method to show a message box within the shell.
        // </summary>
        // <param name="messageText">Text to show.</param>
        // <param name="messageButtons">Buttons which should appear in the dialog.</param>
        // <param name="defaultButton">Default button (invoked when user presses return).</param>
        // <param name="messageIcon">Icon (warning, error, informational, etc.) to display</param>
        // <returns>result corresponding to the button clicked by the user.</returns>
        private static void ShowMessageBox(string messageText, OLEMSGBUTTON messageButtons, OLEMSGDEFBUTTON defaultButton, OLEMSGICON messageIcon)
        {
            ShowMessageBox(messageText, null, messageButtons, defaultButton, messageIcon);
        }

        // <summary>
        //     Helper method to show a message box within the shell.
        // </summary>
        // <param name="messageText">Text to show.</param>
        // <param name="f1Keyword">F1-keyword.</param>
        // <param name="messageButtons">Buttons which should appear in the dialog.</param>
        // <param name="defaultButton">Default button (invoked when user presses return).</param>
        // <param name="messageIcon">Icon (warning, error, informational, etc.) to display</param>
        // <returns>result corresponding to the button clicked by the user.</returns>
        private static DialogResult ShowMessageBox(
            string messageText, string f1Keyword, OLEMSGBUTTON messageButtons,
            OLEMSGDEFBUTTON defaultButton, OLEMSGICON messageIcon)
        {
            var result = 0;
            var uiShell = (IVsUIShell)Package.GetGlobalService(typeof(SVsUIShell));

            if (uiShell != null)
            {
                var rclsidComp = Guid.Empty;
                uiShell.ShowMessageBox(
                        0, ref rclsidComp, "EF Core Power Tools", messageText, f1Keyword, 0, messageButtons, defaultButton, messageIcon, 0, out result);
            }

            return (DialogResult)result;
        }
    }
}

//-------------------------------------------------------------------------------------------------
// <copyright file="AppCommon.cs" company="Outercurve Foundation">
//   Copyright (c) 2004, Outercurve Foundation.
//   This software is released under Microsoft Reciprocal License (MS-RL).
//   The license and further copyright text can be found in the file
//   LICENSE.TXT at the root directory of the distribution.
// </copyright>
// 
// <summary>
// Common utilities for Wix applications.
// </summary>
//-------------------------------------------------------------------------------------------------

namespace Microsoft.Tools.WindowsInstallerXml
{
    using System;
    using System.Collections;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Reflection;

    /// <summary>
    /// Common utilities for Wix applications.
    /// </summary>
    public static class AppCommon
    {
        /// <summary>
        /// Get a set of files that possibly have a search pattern in the path (such as '*').
        /// </summary>
        /// <param name="searchPath">Search path to find files in.</param>
        /// <param name="fileType">Type of file; typically "Source".</param>
        /// <returns>An array of files matching the search path.</returns>
        /// <remarks>
        /// This method is written in this verbose way because it needs to support ".." in the path.
        /// It needs the directory path isolated from the file name in order to use Directory.GetFiles
        /// or DirectoryInfo.GetFiles.  The only way to get this directory path is manually since
        /// Path.GetDirectoryName does not support ".." in the path.
        /// </remarks>
        /// <exception cref="WixFileNotFoundException">Throws WixFileNotFoundException if no file matching the pattern can be found.</exception>
        public static string[] GetFiles(string searchPath, string fileType)
        {
            if (null == searchPath)
            {
                throw new ArgumentNullException("searchPath");
            }

            // convert alternate directory separators to the standard one
            string filePath = searchPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            int lastSeparator = filePath.LastIndexOf(Path.DirectorySeparatorChar);
            string[] files = null;

            try
            {
                if (0 > lastSeparator)
                {
                    files = Directory.GetFiles(".", filePath);
                }
                else // found directory separator
                {
                    files = Directory.GetFiles(filePath.Substring(0, lastSeparator + 1), filePath.Substring(lastSeparator + 1));
                }
            }
            catch (DirectoryNotFoundException)
            {
                // don't let this function throw the DirectoryNotFoundException. (this exception
                // occurs for non-existant directories and invalid characters in the searchPattern)
            }
            catch (ArgumentException)
            {
                // don't let this function throw the ArgumentException. (this exception
                // occurs in certain situations such as when passing a malformed UNC path)
            }
            catch (IOException)
            {
                throw new WixFileNotFoundException(searchPath, fileType);
            }

            // file could not be found or path is invalid in some way
            if (null == files || 0 == files.Length)
            {
                throw new WixFileNotFoundException(searchPath, fileType);
            }

            return files;
        }

        /// <summary>
        /// Read the configuration file (*.exe.config).
        /// </summary>
        /// <param name="extensions">Extensions to load.</param>
        public static void ReadConfiguration(StringCollection extensions)
        {
            if (null == extensions)
            {
                throw new ArgumentNullException("extensions");
            }

            // Don't use the default AppSettings reader because
            // the tool may be called from within another process.
            // Instead, read the .exe.config file from the tool location.
            string toolPath = Assembly.GetCallingAssembly().Location;
            Configuration config = ConfigurationManager.OpenExeConfiguration(toolPath);
            if (config.HasFile)
            {
                KeyValueConfigurationElement configVal = config.AppSettings.Settings["extensions"];
                if (configVal != null)
                {
                    string extensionTypes = configVal.Value;
                    foreach (string extensionType in extensionTypes.Split(";".ToCharArray()))
                    {
                        extensions.Add(extensionType);
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up the temp files.
        /// </summary>
        /// <param name="path">The directory to delete.</param>
        /// <param name="messageHandler">The message handler.</param>
        /// <returns>True if all files were deleted, false otherwise.</returns>
        public static bool DeleteDirectory(string path, IMessageHandler messageHandler)
        {
            // try three times and give up with a warning if the temp files aren't gone by then
            int retryLimit = 3;
            bool removedReadOnly = false;

            for (int i = 0; i < retryLimit; i++)
            {
                try
                {
                    Directory.Delete(path, true);   // toast the whole temp directory
                    break; // no exception means we got success the first time
                }
                catch (UnauthorizedAccessException)
                {
                    if (!removedReadOnly) // should only need to unmark readonly once - there's no point in doing it again and again
                    {
                        removedReadOnly = true;
                        RecursiveFileAttributes(path, FileAttributes.ReadOnly, false); // toasting will fail if any files are read-only. Try changing them to not be.
                    }
                    else
                    {
                        messageHandler.OnMessage(WixWarnings.AccessDeniedForDeletion(null, path));
                        return false;
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    // if the path doesn't exist, then there is nothing for us to worry about
                    break;
                }
                catch (IOException) // directory in use
                {
                    if (i == (retryLimit - 1)) // last try failed still, give up
                    {
                        messageHandler.OnMessage(WixWarnings.DirectoryInUse(null, path));
                        return false;
                    }
                    System.Threading.Thread.Sleep(300);  // sleep a bit before trying again
                }
            }

            return true;
        }

        /// <summary>
        /// Recursively loops through a directory, changing an attribute on all of the underlying files.
        /// An example is to add/remove the ReadOnly flag from each file.
        /// </summary>
        /// <param name="path">The directory path to start deleting from.</param>
        /// <param name="fileAttribute">The FileAttribute to change on each file.</param>
        /// <param name="markAttribute">If true, add the attribute to each file. If false, remove it.</param>
        private static void RecursiveFileAttributes(string path, FileAttributes fileAttribute, bool markAttribute)
        {
            foreach (string subDirectory in Directory.GetDirectories(path))
            {
                RecursiveFileAttributes(subDirectory, fileAttribute, markAttribute);
            }

            foreach (string filePath in Directory.GetFiles(path))
            {
                FileAttributes attributes = File.GetAttributes(filePath);
                if (markAttribute)
                {
                    attributes = attributes | fileAttribute; // add to list of attributes
                }
                else if (fileAttribute == (attributes & fileAttribute)) // if attribute set
                {
                    attributes = attributes ^ fileAttribute; // remove from list of attributes
                }
                File.SetAttributes(filePath, attributes);
            }
        }

        /// <summary>
        /// Prepares the console for localization.
        /// </summary>
        public static void PrepareConsoleForLocalization()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture.GetConsoleFallbackUICulture();
            if ((Console.OutputEncoding.CodePage != Encoding.UTF8.CodePage) &&
                (Console.OutputEncoding.CodePage != Thread.CurrentThread.CurrentUICulture.TextInfo.OEMCodePage) &&
                (Console.OutputEncoding.CodePage != Thread.CurrentThread.CurrentUICulture.TextInfo.ANSICodePage))
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            }
        }

        /// <summary>
        /// Creates and returns the string for CreatingApplication field (MSI Summary Information Stream).
        /// </summary>
        /// <remarks>It reads the AssemblyProductAttribute and AssemblyVersionAttribute of executing assembly
        /// and builds the CreatingApplication string of the form "[ProductName] ([ProductVersion])".</remarks>
        /// <returns>Returns value for PID_APPNAME."</returns>
        public static string GetCreatingApplicationString()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            return WixDistribution.ReplacePlaceholders("[AssemblyProduct] ([FileVersion])", assembly);
        }

        /// <summary>
        /// Displays help message header on Console for caller tool.
        /// </summary>
        public static void DisplayToolHeader()
        {
            Assembly assembly = Assembly.GetCallingAssembly();
            Console.WriteLine(WixDistribution.ReplacePlaceholders(WixDistributionSpecificStrings.ToolsetHelpHeader, assembly));
        }

        /// <summary>
        /// Displays help message header on Console for caller tool.
        /// </summary>
        public static void DisplayToolFooter()
        {
            Console.Write(WixDistribution.ReplacePlaceholders(WixDistributionSpecificStrings.ToolsetHelpFooter, null));
        }
    }
}

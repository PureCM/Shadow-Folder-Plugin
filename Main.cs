using System;
using System.Collections.Generic;
using System.Text;
using PureCM.Server;
using PureCM.Client;
using System.Xml.Linq;
using System.IO;

namespace Plugin_ShadowFolder
{
    [EventHandlerDescription("Plugin that updates a shadow folder files whenever a change is submitted")]
    public class ShadowFolderPlugin : PureCM.Server.Plugin
    {
        private Repository m_oPcmRepository = null;
        private String m_strStream;
        private String m_strPath;

        public override bool OnStart(XElement oConfig, Connection oConnection)
        {
            string strRepository;
            {
                if (oConfig.Element("Repository") != null && oConfig.Element("Repository").Value.Length > 0)
                {
                    strRepository = oConfig.Element("Repository").Value;

                    m_oPcmRepository = oConnection.Repositories.ByName(strRepository);

                    if (m_oPcmRepository == null)
                    {
                        LogError("The repository '" + strRepository + "' does not exist.");
                        return false;
                    }
                }
                else
                {
                    LogError("You must specify a repository in the config file.");
                    return false;
                }
            }

            PureCM.Client.Stream oStream = null;
            {
                if (oConfig.Element("Stream") != null && oConfig.Element("Stream").Value.Length > 0)
                {
                    m_strStream = oConfig.Element("Stream").Value;

                    oStream = m_oPcmRepository.Streams.ByPath(m_strStream);

                    if (oStream != null)
                    {
                        m_strStream = oStream.StreamPath;
                    }
                    else
                    {
                        LogError("The stream '" + m_strStream + "' does not exist.");
                        return false;
                    }
                }
                else
                {
                    LogError("You must specify a stream in the config file.");
                    return false;
                }
            }

            {
                if (oConfig.Element("Path") == null || oConfig.Element("Path").Value.Length == 0)
                {
                    m_strPath = Path.Combine(DataDirectory, m_strStream);
                }
                else
                {
                    m_strPath = oConfig.Element("Path").Value;
                }

                try
                {
                    DirectoryInfo oDir = new DirectoryInfo(m_strPath);

                    if ( !oDir.Exists )
                    {
                        oDir.Create();
                    }
                }
                catch(Exception e)
                {
                    LogError(String.Format("Failed to create directory '{0}' ({1})", m_strPath, e.Message));
                    return false;
                }
            }

            oConnection.OnChangeSubmitted = OnChangeSubmitted;
            oConnection.OnStreamCreated = OnStreamCreated;
            oConnection.OnIdle = OnIdle;

            return true;
        }

        public override void OnStop()
        {
        }

        private void OnStreamCreated(StreamCreatedEvent evt)
        {
            if (evt.Repository != null)
            {
                evt.Repository.RefreshStreams();
            }
        }

        private void OnChangeSubmitted(ChangeSubmittedEvent evt)
        {
            Repository oRepos = evt.Repository;
            PureCM.Client.Stream oStream = evt.Stream;

            if (oRepos == null)
            {
                LogWarning("Repository not found for submitted change!");
                return;
            }

            if (oStream == null)
            {
                LogWarning("Stream not found for submitted change!");
                return;
            }

            LogInfo("Change detected in Repository '" + oRepos.Name + "' " + "and Stream '" + oStream.Name + "'.");

            if ((m_oPcmRepository == null) || (oRepos.Name.Equals(m_oPcmRepository.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                if (oStream.StreamPath.Equals(m_strStream,StringComparison.CurrentCultureIgnoreCase))
                {
                    LogInfo("This change is the correct stream so will attempt to update the workspace.");

                    Workspace oWorkspace = FindWorkspace(oRepos, true);

                    if (oWorkspace != null)
                    {
                        UpdateWorkspace(oWorkspace);
                    }
                }
                else
                {
                    LogInfo(String.Format("This change will be ignored because it is a different stream ('{0}' vs '{1}').",
                                          oStream.StreamPath,
                                          m_strStream));
                }
            }
        }

        private void OnIdle()
        {
            // On startup we want to check for any updates in OnIdle(). After we have done this once
            // we don't process OnIdle() anymore and we just wait for OnChangeSubmitted() events.
            // It would have been tidier to just do this in OnStart() but we want OnStart to return
            // quickly so it is shown as started. Remember that this might take some time because this
            // will create the initial workspace if necessary.
            if (m_oPcmRepository != null)
            {
                Workspace oWorkspace = FindWorkspace(m_oPcmRepository, true);

                if (oWorkspace != null)
                {
                    UpdateWorkspace(oWorkspace);
                }

                // Setting m_oPcmRepository to null will ensure we don't process OnIdle() again
                m_oPcmRepository = null;
            }
        }

        private Workspace FindWorkspace(Repository oRepos, bool bCreate)
        {
            // See if the workspace exists
            {
                Workspaces oWorkspaces = oRepos.Workspaces;

                LogInfo("Looking for workspace '" + m_strPath + "'.");

                foreach (Workspace oWorkspace in oWorkspaces)
                {
                    if (oWorkspace.ManagesPath(m_strPath))
                    {
                        LogInfo("Found workspace '" + m_strPath + "'.");
                        return oWorkspace;
                    }
                }

                LogInfo("Failed to find workspace for path '" + m_strPath + "'.");
            }

            if (bCreate)
            {
                // The workspace doesn't exist - so try and create it
                LogInfo("Workspace '" + m_strPath + "' does not exist. Will try and create...");

                PureCM.Client.Stream oStream = oRepos.Streams.ByPath(m_strStream);

                if (oStream != null)
                {
                    if (oStream.CreateWorkspace("", m_strPath, string.Format("'{0}' Shadow Folder", m_strStream), false, true, true))
                    {
                        LogInfo("Workspace '" + m_strPath + "' has been created.");

                        Workspace oWS = FindWorkspace(oRepos, false);

                        if (oWS != null)
                        {
                            return oWS;
                        }
                        else
                        {
                            LogError("After creating workspace '" + m_strPath + "' the workspace could not be found!");
                        }
                    }
                    else
                    {
                        LogError("Failed to create workspace '" + m_strPath + "'.");
                    }
                }
                else
                {
                    LogError("Failed to create workspace '" + m_strPath + "'. The stream '" + m_strStream + "' is invalid.");
                }
            }

            return null;
        }

        private void UpdateWorkspace(Workspace oWorkspace)
        {
            LogInfo("Updating workspace '" + m_strPath + "'.");
            SDK.TPCMReturnCode tRetCode;

            if (!oWorkspace.UpdateToLatest(out tRetCode))
            {
                LogError("Failed to update workspace '" + m_strPath + "' (" + tRetCode.ToString() + ").");
            }
            else if (tRetCode != SDK.TPCMReturnCode.pcmSuccess)
            {
                LogWarning("Updating workspace '" + m_strPath + "' returned non-successful return code (" + tRetCode.ToString() + ").");
            }
            else
            {
                LogInfo("Successfully Updated workspace '" + m_strPath + "'.");
            }
        }
    }
}

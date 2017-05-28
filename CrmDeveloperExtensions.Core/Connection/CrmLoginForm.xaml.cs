﻿using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CrmDeveloperExtensions.Core.Logging;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.CrmConnectControl;
using Window = System.Windows.Window;

namespace CrmDeveloperExtensions.Core.Connection
{
    /// <summary>
    /// Interaction logic for CRMLoginForm1.xaml
    /// </summary>
    public partial class CrmLoginForm : Window
    {
        #region Vars
        /// <summary>
        /// Microsoft.Xrm.Tooling.Connector services
        /// </summary>
        private CrmServiceClient _crmSvc;
        /// <summary>
        /// Bool flag to determine if there is a connection 
        /// </summary>
        private bool _bIsConnectedComplete;
        /// <summary>
        /// CRM Connection Manager component. 
        /// </summary>
        private CrmConnectionManager _mgr;
        /// <summary>
        ///  This is used to allow the UI to reset w/out closing 
        /// </summary>
        private bool _resetUiFlag;
        #endregion

        #region Properties
        /// <summary>
        /// CRM Connection Manager 
        /// </summary>
        public CrmConnectionManager CrmConnectionMgr => _mgr;

        #endregion

        #region Event
        /// <summary>
        /// Raised when a connection to CRM has completed. 
        /// </summary>
        public event EventHandler ConnectionToCrmCompleted;
        #endregion


        public CrmLoginForm()
        {
            InitializeComponent();
            //// Should be used for testing only.
            //ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
            //{
            //    MessageBox.Show("CertError");
            //    return true;
            //};
            
            EnableXrmToolingLogging();
        }

        /// <summary>
        /// Raised when the window loads for the first time. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            /*
				This is the setup process for the login control, 
				The login control uses a class called CrmConnectionManager to manage the interaction with CRM, this class and also be queried as later points for information about the current connection. 
				In this case, the login control is referred to as CrmLoginCtrl
			 */

            // Set off flag. 
            _bIsConnectedComplete = false;

            // Init the CRM Connection manager.. 
            _mgr = new CrmConnectionManager();
            // Pass a reference to the current UI or container control,  this is used to synchronize UI threads In the login control
            _mgr.ParentControl = CrmLoginCtrl;
            // if you are using an unmanaged client, excel for example, and need to store the config in the users local directory
            // set this option to true. 
            _mgr.UseUserLocalDirectoryForConfigStore = true;
            // if you are using an unmanaged client,  you need to provide the name of an exe to use to create app config key's for. 
            //mgr.HostApplicatioNameOveride = "MyExecName.exe";
            // CrmLoginCtrl is the Login control,  this sets the CrmConnection Manager into it. 
            CrmLoginCtrl.SetGlobalStoreAccess(_mgr);
            // There are several modes to the login control UI
            CrmLoginCtrl.SetControlMode(ServerLoginConfigCtrlMode.FullLoginPanel);
            // this wires an event that is raised when the login button is pressed. 
            CrmLoginCtrl.ConnectionCheckBegining += new EventHandler(CrmLoginCtrl_ConnectionCheckBegining);
            // this wires an event that is raised when an error in the connect process occurs. 
            CrmLoginCtrl.ConnectErrorEvent += new EventHandler<ConnectErrorEventArgs>(CrmLoginCtrl_ConnectErrorEvent);
            // this wires an event that is raised when a status event is returned. 
            CrmLoginCtrl.ConnectionStatusEvent += new EventHandler<ConnectStatusEventArgs>(CrmLoginCtrl_ConnectionStatusEvent);
            // this wires an event that is raised when the user clicks the cancel button. 
            CrmLoginCtrl.UserCancelClicked += new EventHandler(CrmLoginCtrl_UserCancelClicked);
            // Check to see if its possible to do an Auto Login 
            if (!_mgr.RequireUserLogin())
            {
                if (MessageBox.Show("Credentials already saved in configuration\nChoose Yes to Auto Login or No to Reset Credentials", "Auto Login", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    // If RequireUserLogin is false, it means that there has been a successful login here before and the credentials are cached. 
                    CrmLoginCtrl.IsEnabled = false;
                    // When running an auto login,  you need to wire and listen to the events from the connection manager.
                    // Run Auto User Login process, Wire events. 
                    _mgr.ServerConnectionStatusUpdate += new EventHandler<ServerConnectStatusEventArgs>(mgr_ServerConnectionStatusUpdate);
                    _mgr.ConnectionCheckComplete += new EventHandler<ServerConnectStatusEventArgs>(mgr_ConnectionCheckComplete);
                    // Start the connection process. 
                    _mgr.ConnectToServerCheck();

                    // Show the message grid. 
                    CrmLoginCtrl.ShowMessageGrid();
                }
            }
        }

        #region Events

        /// <summary>
        /// Updates from the Auto Login process. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mgr_ServerConnectionStatusUpdate(object sender, ServerConnectStatusEventArgs e)
        {
            // The Status event will contain information about the current login process,  if Connected is false, then there is not yet a connection. 
            // Set the updated status of the loading process. 
            Dispatcher.Invoke(DispatcherPriority.Normal,
                               new Action(() =>
                               {
                                   Title = string.IsNullOrWhiteSpace(e.StatusMessage) ? e.ErrorMessage : e.StatusMessage;
                               }));

        }

        /// <summary>
        /// Complete Event from the Auto Login process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mgr_ConnectionCheckComplete(object sender, ServerConnectStatusEventArgs e)
        {
            // The Status event will contain information about the current login process,  if Connected is false, then there is not yet a connection. 
            // Unwire events that we are not using anymore, this prevents issues if the user uses the control after a failed login. 
            ((CrmConnectionManager)sender).ConnectionCheckComplete -= mgr_ConnectionCheckComplete;
            ((CrmConnectionManager)sender).ServerConnectionStatusUpdate -= mgr_ServerConnectionStatusUpdate;

            if (!e.Connected)
            {
                // if its not connected pop the login screen here. 
                if (e.MultiOrgsFound)
                    MessageBox.Show("Unable to Login to CRM using cached credentials. Org Not found", "Login Failure");
                else
                    MessageBox.Show("Unable to Login to CRM using cached credentials", "Login Failure");

                _resetUiFlag = true;
                CrmLoginCtrl.GoBackToLogin();
                // Bad Login Get back on the UI. 
                Dispatcher.Invoke(DispatcherPriority.Normal,
                       new Action(() =>
                       {
                           Title = "Failed to Login with cached credentials.";
                           MessageBox.Show(Title, "Notification from ConnectionManager", MessageBoxButton.OK, MessageBoxImage.Error);
                           CrmLoginCtrl.IsEnabled = true;
                       }
                        ));
                _resetUiFlag = false;
            }
            else
            {
                // Good Login Get back on the UI 
                if (e.Connected && !_bIsConnectedComplete)
                    ProcessSuccess();
            }
        }

        /// <summary>
        ///  Login control connect check starting. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CrmLoginCtrl_ConnectionCheckBegining(object sender, EventArgs e)
        {
            _bIsConnectedComplete = false;
            Dispatcher.Invoke(DispatcherPriority.Normal,
                               new Action(() =>
                               {
                                   this.Title = "Starting Login Process. ";
                                   CrmLoginCtrl.IsEnabled = true;
                               }
                                   ));
        }

        /// <summary>
        /// Login control connect check status event. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CrmLoginCtrl_ConnectionStatusEvent(object sender, ConnectStatusEventArgs e)
        {
            //Here we are using the bIsConnectedComplete bool to check to make sure we only process this call once. 
            if (e.ConnectSucceeded && !_bIsConnectedComplete)
                ProcessSuccess();

        }

        /// <summary>
        /// Login control Error event. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CrmLoginCtrl_ConnectErrorEvent(object sender, ConnectErrorEventArgs e)
        {
            //MessageBox.Show(e.ErrorMessage, "Error here");
        }

        /// <summary>
        /// Login Control Cancel event raised. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CrmLoginCtrl_UserCancelClicked(object sender, EventArgs e)
        {
            if (!_resetUiFlag)
                this.Close();
        }

        #endregion

        /// <summary>
        /// This raises and processes Success
        /// </summary>
        private void ProcessSuccess()
        {
            _resetUiFlag = true;
            _bIsConnectedComplete = true;
            _crmSvc = _mgr.CrmSvc;
            CrmLoginCtrl.GoBackToLogin();
            Dispatcher.Invoke(DispatcherPriority.Normal,
               new Action(() =>
                   {
                       Title = "Notification from Parent";
                       CrmLoginCtrl.IsEnabled = true;
                   }
                ));

            // Notify Caller that we are done with success. 
            ConnectionToCrmCompleted?.Invoke(this, null);

            _resetUiFlag = false;
        }

        private void EnableXrmToolingLogging()
        {
            DTE dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (!UserOptionsGrid.GetLoggingOptionBoolean(dte, "ExtensionLoggingEnabled"))
                return;

            TraceControlSettings.TraceLevel = SourceLevels.All;
            string logPath = XrmToolingLogging.GetLogFilePath(dte);
            TraceControlSettings.AddTraceListener(new TextWriterTraceListener(logPath));
        }
    }

    #region system.diagnostics settings for this control

    // Add or merge this section to your app to enable diagnostics on the use of the CRM login control and connection
    /*
  <system.diagnostics>
    <trace autoflush="true" />
    <sources>
      <source name="Microsoft.Xrm.Tooling.Connector.CrmServiceClient"
              switchName="Microsoft.Xrm.Tooling.Connector.CrmServiceClient"
              switchType="System.Diagnostics.SourceSwitch">
        <listeners>
          <add name="console" type="System.Diagnostics.DefaultTraceListener" />
          <remove name="Default"/>
          <add name ="fileListener"/>
        </listeners>
      </source>
      <source name="Microsoft.Xrm.Tooling.CrmConnectControl"
              switchName="Microsoft.Xrm.Tooling.CrmConnectControl"
              switchType="System.Diagnostics.SourceSwitch">
        <listeners>
          <add name="console" type="System.Diagnostics.DefaultTraceListener" />
          <remove name="Default"/>
          <add name ="fileListener"/>
        </listeners>
      </source>

      <source name="Microsoft.Xrm.Tooling.WebResourceUtility"
              switchName="Microsoft.Xrm.Tooling.WebResourceUtility"
              switchType="System.Diagnostics.SourceSwitch">
        <listeners>
          <add name="console" type="System.Diagnostics.DefaultTraceListener" />
          <remove name="Default"/>
          <add name ="fileListener"/>
        </listeners>
      </source>
      
    <!-- WCF DEBUG SOURCES -->
      <source name="System.IdentityModel" switchName="System.IdentityModel">
        <listeners>
          <add name="xml" />
        </listeners>
      </source>
      <!-- Log all messages in the 'Messages' tab of SvcTraceViewer. -->
      <source name="System.ServiceModel.MessageLogging" switchName="System.ServiceModel.MessageLogging" >
        <listeners>
          <add name="xml" />
        </listeners>
      </source>
      <!-- ActivityTracing and propogateActivity are used to flesh out the 'Activities' tab in
           SvcTraceViewer to aid debugging. -->
      <source name="System.ServiceModel" switchName="System.ServiceModel" propagateActivity="true">
        <listeners>
          <add name="xml" />
        </listeners>
      </source>
      <!-- END WCF DEBUG SOURCES -->
    </sources>
    <switches>
      <!-- 
            Possible values for switches: Off, Error, Warining, Info, Verbose
                Verbose:    includes Error, Warning, Info, Trace levels
                Info:       includes Error, Warning, Info levels
                Warning:    includes Error, Warning levels
                Error:      includes Error level
        -->
      <add name="Microsoft.Xrm.Tooling.Connector.CrmServiceClient" value="Verbose" />
      <add name="Microsoft.Xrm.Tooling.CrmConnectControl" value="Verbose"/>
      <add name="Microsoft.Xrm.Tooling.WebResourceUtility" value="Verbose" />
      <add name="System.IdentityModel" value="Verbose"/>
      <add name="System.ServiceModel.MessageLogging" value="Verbose"/>
      <add name="System.ServiceModel" value="Error, ActivityTracing"/>
      
    </switches>
    <sharedListeners>
      <add name="fileListener" type="System.Diagnostics.TextWriterTraceListener" initializeData="LoginControlTesterLog.txt"/>
      <!--<add name="eventLogListener" type="System.Diagnostics.EventLogTraceListener" initializeData="CRM UII"/>-->
      <add name="xml" type="System.Diagnostics.XmlWriterTraceListener" initializeData="CrmToolBox.svclog" />
    </sharedListeners>
  </system.diagnostics>
*/

    #endregion
}

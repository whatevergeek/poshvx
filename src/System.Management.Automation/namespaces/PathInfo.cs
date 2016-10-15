/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using Dbg = System.Management.Automation;

namespace System.Management.Automation
{
    /// <summary>
    /// An object that represents a path.
    /// </summary>
    public sealed class PathInfo
    {
        /// <summary>
        /// Gets the drive that contains the path.
        /// </summary>
        public PSDriveInfo Drive
        {
            get
            {
                PSDriveInfo result = null;

                if (_drive != null &&
                    !_drive.Hidden)
                {
                    result = _drive;
                }

                return result;
            }
        } // Drive

        /// <summary>
        /// Gets the provider that contains the path.
        /// </summary>
        public ProviderInfo Provider
        {
            get
            {
                return _provider;
            }
        } // Provider

        /// <summary>
        /// This is the internal mechanism to get the hidden drive.
        /// </summary>
        ///
        /// <returns>
        /// The drive associated with this PathInfo.
        /// </returns>
        ///
        internal PSDriveInfo GetDrive()
        {
            return _drive;
        } // GetDrive

        /// <summary>
        /// Gets the provider internal path for the PSPath that this PathInfo represents.
        /// </summary>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// The provider encountered an error when resolving the path.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// The path was a home relative path but the home path was not
        /// set for the provider.
        /// </exception>
        public string ProviderPath
        {
            get
            {
                if (_providerPath == null)
                {
                    // Construct the providerPath

                    LocationGlobber pathGlobber = _sessionState.Internal.ExecutionContext.LocationGlobber;
                    _providerPath = pathGlobber.GetProviderPath(Path);
                }

                return _providerPath;
            }
        }
        private string _providerPath;
        private SessionState _sessionState;

        /// <summary>
        /// Gets the MSH path that this object represents.
        /// </summary>
        public string Path
        {
            get
            {
                return this.ToString();
            }
        } // Path

        private PSDriveInfo _drive;
        private ProviderInfo _provider;
        private string _path = String.Empty;

        /// <summary>
        /// Gets a string representing the MSH path.
        /// </summary>
        ///
        /// <returns>
        /// A string representing the MSH path.
        /// </returns>
        public override string ToString()
        {
            string result = _path;


            if (_drive == null ||
                _drive.Hidden)
            {
                // For hidden drives just return the current location
                result =
                    LocationGlobber.GetProviderQualifiedPath(
                        _path,
                        _provider);
            }
            else
            {
                result = LocationGlobber.GetDriveQualifiedPath(_path, _drive);
            }

            return result;
        } // ToString

        /// <summary>
        /// The constructor of the PathInfo object.
        /// </summary>
        ///
        /// <param name="drive">
        /// The drive that contains the path
        /// </param>
        ///
        /// <param name="provider">
        /// The provider that contains the path.
        /// </param>
        /// 
        /// <param name="path">
        /// The path this object represents.
        /// </param>
        ///
        /// <param name="sessionState">
        /// The session state associated with the drive, provider, and path information.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="drive"/>, <paramref name="provider"/>,
        /// <paramref name="path"/>, or <paramref name="sessionState"/> is null.
        /// </exception>
        /// 
        internal PathInfo(PSDriveInfo drive, ProviderInfo provider, string path, SessionState sessionState)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (sessionState == null)
            {
                throw PSTraceSource.NewArgumentNullException("sessionState");
            }

            _drive = drive;
            _provider = provider;
            _path = path;
            _sessionState = sessionState;
        } // constructor
    } // PathInfo
} // namespace System.Management.Automation

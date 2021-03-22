﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Revit.TestRunner.Shared.Communication.Dto;

namespace Revit.TestRunner.Shared.Communication.Client
{
    /// <summary>
    /// Generic client for HTTP like file based communication.
    /// </summary>
    public class FileClient
    {
        private readonly string mClientName;
        private readonly string mClientVersion;

        /// <summary>
        /// Constructor. Create Folder if not exist.
        /// </summary>
        public FileClient( string basePath, string clientName = "", string clientVersion = "" )
        {
            BasePath = basePath;
            mClientName = clientName;
            mClientVersion = clientVersion;

            if( !Directory.Exists( BasePath ) ) {
                Directory.CreateDirectory( BasePath );
            }
        }

        /// <summary>
        /// Get the base path.
        /// </summary>
        public string BasePath { get; }

        /// <summary>
        /// Post a request json object and get back a response json object.
        /// Path can be absolute or relative. Must be under <see cref="BasePath"/>.
        /// </summary>
        public async Task<TResponse> GetJson<TResponse>( string path, CancellationToken cancellationToken ) where TResponse : BaseResponseDto
        {
            return await GetJson<NoParameterDto, TResponse>( path, new NoParameterDto(), cancellationToken );
        }

        /// <summary>
        /// Post a request json object and get back a response json object.
        /// Path can be absolute or relative. Must be under <see cref="BasePath"/>.
        /// Timeout 10s.
        /// </summary>
        public async Task<TResponse> GetJson<TRequest, TResponse>( string path, TRequest request, CancellationToken cancellationToken )
            where TResponse : BaseResponseDto
            where TRequest : BaseRequestDto
        {
            const int timeout = 10000;
            string id = Shared.Client.Client.GenerateId();

            request.RequestId = id;
            request.ClientName = mClientName;
            request.ClientVersion = mClientVersion;
            request.Timestamp = DateTime.Now;

            string requestString = JsonHelper.ToString( request );
            string responseString = null;

            string absolutePath = GetAbsolutePath( path );
            string requestFilePath = Path.Combine( absolutePath, $"{id}.request" );
            string responseFilePath = Path.Combine( absolutePath, $"{id}.response" );

            FileHelper.WriteStringWithLock( requestFilePath, requestString );

            Task responseTask = new Task( () => {
                while( responseString == null && !cancellationToken.IsCancellationRequested ) {
                    responseString = FileHelper.ReadStringWithLock( responseFilePath );

                    if( responseString != null ) {
                        FileHelper.DeleteWithLock( responseFilePath );
                    }
                    else {
                        Thread.Sleep( 500 );
                    }
                }
            } );

            responseTask.Start();

            if( await Task.WhenAny( responseTask, Task.Delay( timeout, cancellationToken ) ) == responseTask ) {
            }
            else {
                FileHelper.DeleteWithLock( requestFilePath );
            }

            return !string.IsNullOrEmpty( responseString )
                ? JsonHelper.FromString<TResponse>( responseString )
                : default;
        }
        
        /// <summary>
        /// Check if the given path is valid. Can be absolut ore relative.
        /// Directory must exist and must be under <see cref="BasePath"/>.
        /// </summary>
        private string GetAbsolutePath( string path )
        {
            string absolutPath = null;

            if( Directory.Exists( path ) ) {
                absolutPath = path;
            }
            else if( Directory.Exists( Path.Combine( BasePath, path ) ) ) {
                absolutPath = Path.Combine( BasePath, path );
            }

            if( string.IsNullOrEmpty( absolutPath ) || !absolutPath.StartsWith( BasePath ) ) {
                throw new NotFoundException( absolutPath );
            }

            return absolutPath;
        }

    }

    public class NotFoundException : Exception
    {
        public NotFoundException( string path )
        {
            Path = path;
        }

        public string Path { get; }
    }
}

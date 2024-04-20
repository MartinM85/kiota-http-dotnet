﻿// ------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the MIT License.  See License in the project root for license information.
// ------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Http.HttpClientLibrary.Extensions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

namespace Microsoft.Kiota.Http.HttpClientLibrary.Middleware
{
    /// <summary>
    /// A <see cref="TelemetryHandler"/> implementation using standard .NET libraries.
    /// </summary>
    public class TelemetryHandler : DelegatingHandler
    {
        private readonly TelemetryHandlerOption _telemetryHandlerOption;

        /// <summary>
        /// The <see cref="TelemetryHandlerOption"/> constructor
        /// </summary>
        /// <param name="telemetryHandlerOption">The <see cref="TelemetryHandlerOption"/> instance to configure the telemetry</param>
        public TelemetryHandler(TelemetryHandlerOption? telemetryHandlerOption = null)
        {
            this._telemetryHandlerOption = telemetryHandlerOption ?? new TelemetryHandlerOption();
        }

        /// <summary>
        /// Send a HTTP request
        /// </summary>
        /// <param name="request">The HTTP request<see cref="HttpRequestMessage"/>needs to be sent.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns></returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if(request == null)
                throw new ArgumentNullException(nameof(request));

            var telemetryHandlerOption = request.GetRequestOption<TelemetryHandlerOption>() ?? _telemetryHandlerOption;

            // use the enriched request from the handler
            if(telemetryHandlerOption.TelemetryConfigurator != null)
            {
                var enrichedRequest = telemetryHandlerOption.TelemetryConfigurator(request);
                return await base.SendAsync(enrichedRequest, cancellationToken).ConfigureAwait(false);
            }

            // Just forward the request if TelemetryConfigurator was intentionally set to null
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}

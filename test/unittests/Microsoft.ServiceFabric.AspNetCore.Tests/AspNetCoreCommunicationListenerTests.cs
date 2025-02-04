// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.AspNetCore.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.Threading;
    using FluentAssertions;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
    using Moq;

    /// <summary>
    /// Test class for AspNetCoreCommunicationListener.
    /// </summary>
    public class AspNetCoreCommunicationListenerTests
    {
        /// <summary>
        /// Endpoint name used by tests.
        /// </summary>
        protected const string EndpointName = "MockEndpoint";

        private const EndpointProtocol ExpectedProtocolFromEndpoint = EndpointProtocol.Http;
        private const string DefaultExpectedProtocol = "http";
        private const string DefaultPathPrefix = "PathPrefix";
        private const int DefaultExpectedPort = 0;
        private string urlFromListenerToBuildFunc;

        /// <summary>
        /// Gets strings for the two Host Types - WebHost and GenericHost.
        /// </summary>
        public static IEnumerable<object[]> HostTypes
        {
            get
            {
                return new List<object[]>
                {
                    new object[] { "WebHost" },
                    new object[] { "GenericHost" },
                };
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether WebHost/GenericHost was started/stopped.
        /// </summary>
        protected bool IsStarted { get; set; }

        /// <summary>
        /// Gets or sets the listener used by the test.
        /// </summary>
        protected AspNetCoreCommunicationListener Listener { get;  set; }

        /// <summary>
        /// Gets or sets the ServiceFabricIntegrationOption.
        /// </summary>
        protected ServiceFabricIntegrationOptions IntegrationOptions { get; set; }

        /// <summary>
        /// Gets the EndpointResourceDescription used by the test.
        /// </summary>
        /// <returns>EndpointResourceDescription isntance.</returns>
        protected EndpointResourceDescription GetTestEndpoint()
        {
            return new EndpointResourceDescription()
            {
                Name = EndpointName,
                Protocol = ExpectedProtocolFromEndpoint,

                // Port = ExpectedPortFromEndpoint
            };
        }

        /// <summary>
        /// Method to build Microsoft.AspNetCore.Hosting.IWebHost.
        /// </summary>
        /// <param name="url">Endpoint url generated by the listener.</param>
        /// <param name="listener">Listener instance.</param>
        /// <returns>IWebHost which will be used by Communication Listener.</returns>
        protected IWebHost IWebHostBuildFunc(string url, AspNetCoreCommunicationListener listener)
        {
            this.urlFromListenerToBuildFunc = url;

            // Create mock IServerAddressesFeature and set required things used by tests.
            var mockServerAddressFeature = new Mock<IServerAddressesFeature>();
            mockServerAddressFeature.Setup(y => y.Addresses).Returns(new string[] { url });
            var featureCollection = new FeatureCollection();
            featureCollection.Set(mockServerAddressFeature.Object);

            // Create mock IWebHost and set required things used by tests.
            var mockWebHost = new Mock<IWebHost>();
            mockWebHost.Setup(y => y.ServerFeatures).Returns(featureCollection);

            // setup call backs for Start , Dispose.
            mockWebHost.Setup(y => y.StartAsync(CancellationToken.None)).Callback(() => this.IsStarted = true);
            mockWebHost.Setup(y => y.Dispose()).Callback(() => this.IsStarted = false);

            // tell listener whether to generate UniqueServiceUrls
            if (this.IntegrationOptions.Equals(ServiceFabricIntegrationOptions.UseUniqueServiceUrl))
            {
                listener.ConfigureToUseUniqueServiceUrl();
            }

            return mockWebHost.Object;
        }

        /// <summary>
        /// Method to build Microsoft.Extensions.Hosting.IHost.
        /// </summary>
        /// <param name="url">Endpoint url generated by the listener.</param>
        /// <param name="listener">Listener instance.</param>
        /// <returns>IHost which will be used by Communication Listener.</returns>
        protected IHost IHostBuildFunc(string url, AspNetCoreCommunicationListener listener)
        {
            this.urlFromListenerToBuildFunc = url;

            // Create mock IServerAddressesFeature and set required things used by tests.
            var mockServerAddressFeature = new Mock<IServerAddressesFeature>();
            mockServerAddressFeature.Setup(y => y.Addresses).Returns(new string[] { url });
            var featureCollection = new FeatureCollection();
            featureCollection.Set(mockServerAddressFeature.Object);

            // Create mock IServer and set required things used by tests.
            var mockServer = new Mock<IServer>();
            mockServer.Setup(y => y.Features).Returns(featureCollection);

            // Create ServiceCollection and IServiceProvider and set required things used for tests.
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IServer>(mockServer.Object);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Create mock IHost and set required things used by tests.
            var mockHost = new Mock<IHost>();
            mockHost.Setup(y => y.Services).Returns(serviceProvider);

            // setup call backs for Start , Dispose.
            mockHost.Setup(y => y.StartAsync(CancellationToken.None)).Callback(() => this.IsStarted = true);
            mockHost.Setup(y => y.StopAsync(CancellationToken.None)).Callback(() => this.IsStarted = false);
            mockHost.Setup(y => y.Dispose()).Callback(() => this.IsStarted = false);

            // tell listener whether to generate UniqueServiceUrls
            if (this.IntegrationOptions.Equals(ServiceFabricIntegrationOptions.UseUniqueServiceUrl))
            {
                listener.ConfigureToUseUniqueServiceUrl();
            }

            return mockHost.Object;
        }

        /// <summary>
        /// Tests Url for ServiceFabricIntegrationOptions.UseUniqueServiceUrl
        /// 1. When endpoint name is provided (protocol and port comes from endpoint.) :
        ///   a. url given to Func to create IWebHost/IHost should be protocol://+:port.
        ///   b. url returned from OpenAsync should be protocol://IPAddressOrFQDN:port/PartitionId/ReplicaId.
        ///
        /// </summary>
        protected void UseUniqueServiceUrlOptionVerifier()
        {
            this.IntegrationOptions = ServiceFabricIntegrationOptions.UseUniqueServiceUrl;
            var expectedProtocol = ExpectedProtocolFromEndpoint.ToString().ToLowerInvariant();
            var expectedPort = DefaultExpectedPort;

            Console.WriteLine("Starting Verification of urls with UseUniqueServiceUrl option when endpoint ref is provided");
            var context = this.Listener.ServiceContext;
            var expectedUrlToBuildFunc = string.Format("{0}://+:{1}", expectedProtocol, expectedPort);
            var expectedUrlFromOpen = string.Format("{0}://{1}:{2}/{3}/{4}", expectedProtocol, context.NodeContext.IPAddressOrFQDN, expectedPort, context.PartitionId, context.ReplicaOrInstanceId);
            var actualUrlFromOpen = this.Listener.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

            this.urlFromListenerToBuildFunc.Should().Be(expectedUrlToBuildFunc, "listener generates the url.");
            actualUrlFromOpen.Should().Be(actualUrlFromOpen, "url from OpenAsync with endpoint ref");
            Console.WriteLine("Completed Verification of urls with endpoint ref");
        }

        /// <summary>
        /// Tests Url for ServiceFabricIntegrationOptions.None
        /// 1. When endpoint name is provided (protocol and port comes from endpoint.) :
        ///   a. url given to Func to create IWebHost/IHost should be protocol://+:port.
        ///   b. url returned from OpenAsync should be protocol://IPAddressOrFQDN:port.
        ///
        /// </summary>
        protected void WithoutUseUniqueServiceUrlOptionVerifier()
        {
            this.IntegrationOptions = ServiceFabricIntegrationOptions.None;
            var expectedProtocol = ExpectedProtocolFromEndpoint.ToString().ToLowerInvariant();
            var expectedPort = DefaultExpectedPort;

            Console.WriteLine("Starting Verification of urls with UseUniqueServiceUrl option when endpoint ref is provided");
            var context = this.Listener.ServiceContext;
            var expectedUrlToBuildFunc = string.Format("{0}://+:{1}", expectedProtocol, expectedPort);
            var expectedUrlFromOpen = string.Format("{0}://{1}:{2}", expectedProtocol, context.NodeContext.IPAddressOrFQDN, expectedPort);
            var actualUrlFromOpen = this.Listener.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

            this.urlFromListenerToBuildFunc.Should().Be(expectedUrlToBuildFunc, "url to Build Func is generated by listener");
            actualUrlFromOpen.Should().Be(expectedUrlFromOpen, "url from OpenAsync with endpoint ref");
            Console.WriteLine("Completed Verification of urls with endpoint ref");
        }

        /// <summary>
        /// Verify Listener Open and Close.
        /// </summary>
        protected void ListenerOpenCloseVerifier()
        {
            // Open Close
            this.Listener.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            this.IsStarted.Should().BeTrue();

            this.Listener.CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            this.IsStarted.Should().BeFalse();

            // Open Abort
            this.Listener.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            this.IsStarted.Should().BeTrue();

            this.Listener.Abort();
            this.IsStarted.Should().BeFalse();
        }

        /// <summary>
        /// InvalidOperationException is thrown when Endpoint is not found in service manifest.
        /// </summary>
        protected void ExceptionForEndpointNotFoundVerifier()
        {
            Action action = () => this.Listener.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            action.Should().Throw<InvalidOperationException>();
        }
    }
}

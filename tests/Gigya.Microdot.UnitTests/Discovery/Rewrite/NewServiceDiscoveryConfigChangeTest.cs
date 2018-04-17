﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gigya.Microdot.Configuration;
using Gigya.Microdot.Fakes;
using Gigya.Microdot.Interfaces.Configuration;
using Gigya.Microdot.ServiceDiscovery;
using Gigya.Microdot.ServiceDiscovery.Rewrite;
using Gigya.Microdot.SharedLogic;
using Gigya.Microdot.Testing.Shared;
using Gigya.Microdot.Testing.Shared.Utils;
using Ninject;
using NUnit.Framework;
using Shouldly;

namespace Gigya.Microdot.UnitTests.Discovery.Rewrite
{
    [TestFixture]
    public class NewServiceDiscoveryConfigChangeTest
    {
        private NewServiceDiscovery _serviceDiscovery;
        private Dictionary<string, string> _configDic;
        private ManualConfigurationEvents _configRefresh;
        private TestingKernel<ConsoleLog> _unitTestingKernel;
        private ConsulClientMock _consulClientMock;
        public const int Repeat = 1;
        private const string ServiceVersion = "1.0.0.0";

        [SetUp]
        public async Task Setup()
        {
            _configDic = new Dictionary<string, string>();
            _unitTestingKernel = new TestingKernel<ConsoleLog>(k =>
            {
                k.Rebind<INodeSourceLoader>().To<NodeSourceLoader>().InSingletonScope();
                k.Rebind<IEnvironmentVariableProvider>().To<EnvironmentVariableProvider>();
                _consulClientMock = new ConsulClientMock();
                _consulClientMock.SetResult(new EndPointsResult { EndPoints = new[] { new ConsulEndPoint { HostName = "dumy", Version = ServiceVersion } }, ActiveVersion = ServiceVersion, IsQueryDefined = true });
                k.Rebind<Func<string,IConsulClient>>().ToMethod(c=>s=>_consulClientMock);
            }, _configDic);

            _configRefresh = _unitTestingKernel.Get<ManualConfigurationEvents>();
            _serviceDiscovery = _unitTestingKernel.Get<Func<string, ReachabilityChecker, NewServiceDiscovery>>()("ServiceName", x => Task.FromResult(true));
           }

        [TearDown]
        public void TearDown()
        {
            _unitTestingKernel.Dispose();
            _consulClientMock.Dispose();
        }

        [TestCase("Services.ServiceName")]
        [Repeat(Repeat)]

        public async Task DiscoveySettingAreUpdateOnConfigChange(string serviceName)
        {
            await WaitForConfigChange(() =>
                  {
                      _configDic[$"Discovery.{serviceName}.Source"] = "Config";
                      _configDic[$"Discovery.{serviceName}.Hosts"] = "localhost";
                  });

            Assert.AreEqual("Config", _serviceDiscovery.LastServiceConfig.Source);
        }

        [TestCase("Services.OtherServiceName")]
        [Repeat(Repeat)]

        public void DiscoverySettingUpdatingUnrelatedConfig_ShouldNotChange(string serviceName)
        {

            Func<Task> act = () => WaitForConfigChange(() =>
            {
                _configDic[$"Discovery.{serviceName}.Source"] = "Config";
                _configDic[$"Discovery.{serviceName}.Hosts"] = "localhost";
            });

            act.ShouldThrowAsync<TimeoutException>();
        }


        [TestCase("Services.ServiceName")]
        [Repeat(Repeat)]

        public async Task HostShouldUpdateFromConfig(string serviceName)
        {

            await WaitForConfigChange(() =>
            {
                _configDic[$"Discovery.{serviceName}.Source"] = "Config";
                _configDic[$"Discovery.{serviceName}.Hosts"] = "host3";
            });
            var nextHost = _serviceDiscovery.GetNextHost();
            Assert.AreEqual("host3", nextHost.Result.HostName);
        }

        [TestCase("Services.ServiceName")]
        [Repeat(Repeat)]

        public async Task ServiceSourceIsLocal(string serviceName)
        {
            await WaitForConfigChange(() =>
            _configDic[$"Discovery.{serviceName}.Source"] = "Local"
            );
            var remoteHostPull = _serviceDiscovery.GetNextHost();
            remoteHostPull.Result.HostName.ShouldContain(CurrentApplicationInfo.HostName);
        }



        private async Task WaitForConfigChange(Action update)
        {
            try {await _serviceDiscovery.GetNextHost();} catch {}

            var task = _serviceDiscovery.EndPointsChanged.WhenEventReceived();
            update();
            _configRefresh.RaiseChangeEvent();

            await task;
        }
    }
}
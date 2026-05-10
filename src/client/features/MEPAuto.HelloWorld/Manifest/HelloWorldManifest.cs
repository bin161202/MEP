using System;
using MEPAuto.Contracts.Manifests;
using MEPAuto.HelloWorld.Commands;

namespace MEPAuto.HelloWorld.Manifest
{
    public class HelloWorldManifest : IFeatureManifest
    {
        public string Name => "HelloWorld";
        public string DisplayName => "Hello World";
        public string ServerEndpoint => "/api/v1/helloworld/execute";
        public string LicenseFeature => "helloworld.basic";
        public string PanelGroup => "MEPAuto - Demo";
        public int Order => 10;
        public string IconResourcePath => "Icons/hello.png";
        public Type CommandType => typeof(HelloWorldCommand);
    }
}

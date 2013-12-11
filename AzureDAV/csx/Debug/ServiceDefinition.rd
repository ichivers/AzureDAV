<?xml version="1.0" encoding="utf-8"?>
<serviceModel xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" name="AzureDAV" generation="1" functional="0" release="0" Id="f0e91ca5-e339-4a04-82b5-a927247c48be" dslVersion="1.2.0.0" xmlns="http://schemas.microsoft.com/dsltools/RDSM">
  <groups>
    <group name="AzureDAVGroup" generation="1" functional="0" release="0">
      <componentports>
        <inPort name="WebDAV:Endpoint1" protocol="http">
          <inToChannel>
            <lBChannelMoniker name="/AzureDAV/AzureDAVGroup/LB:WebDAV:Endpoint1" />
          </inToChannel>
        </inPort>
      </componentports>
      <settings>
        <aCS name="WebDAV:Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" defaultValue="">
          <maps>
            <mapMoniker name="/AzureDAV/AzureDAVGroup/MapWebDAV:Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" />
          </maps>
        </aCS>
        <aCS name="WebDAVInstances" defaultValue="[1,1,1]">
          <maps>
            <mapMoniker name="/AzureDAV/AzureDAVGroup/MapWebDAVInstances" />
          </maps>
        </aCS>
      </settings>
      <channels>
        <lBChannel name="LB:WebDAV:Endpoint1">
          <toPorts>
            <inPortMoniker name="/AzureDAV/AzureDAVGroup/WebDAV/Endpoint1" />
          </toPorts>
        </lBChannel>
      </channels>
      <maps>
        <map name="MapWebDAV:Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" kind="Identity">
          <setting>
            <aCSMoniker name="/AzureDAV/AzureDAVGroup/WebDAV/Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" />
          </setting>
        </map>
        <map name="MapWebDAVInstances" kind="Identity">
          <setting>
            <sCSPolicyIDMoniker name="/AzureDAV/AzureDAVGroup/WebDAVInstances" />
          </setting>
        </map>
      </maps>
      <components>
        <groupHascomponents>
          <role name="WebDAV" generation="1" functional="0" release="0" software="C:\Users\Ian\Documents\Visual Studio Projects\WebDAV\AzureDAV\csx\Debug\roles\WebDAV" entryPoint="base\x64\WaHostBootstrapper.exe" parameters="base\x64\WaIISHost.exe " memIndex="1792" hostingEnvironment="frontendadmin" hostingEnvironmentVersion="2">
            <componentports>
              <inPort name="Endpoint1" protocol="http" portRanges="80" />
            </componentports>
            <settings>
              <aCS name="Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" defaultValue="" />
              <aCS name="__ModelData" defaultValue="&lt;m role=&quot;WebDAV&quot; xmlns=&quot;urn:azure:m:v1&quot;&gt;&lt;r name=&quot;WebDAV&quot;&gt;&lt;e name=&quot;Endpoint1&quot; /&gt;&lt;/r&gt;&lt;/m&gt;" />
            </settings>
            <resourcereferences>
              <resourceReference name="DiagnosticStore" defaultAmount="[4096,4096,4096]" defaultSticky="true" kind="Directory" />
              <resourceReference name="EventStore" defaultAmount="[1000,1000,1000]" defaultSticky="false" kind="LogStore" />
            </resourcereferences>
          </role>
          <sCSPolicy>
            <sCSPolicyIDMoniker name="/AzureDAV/AzureDAVGroup/WebDAVInstances" />
            <sCSPolicyUpdateDomainMoniker name="/AzureDAV/AzureDAVGroup/WebDAVUpgradeDomains" />
            <sCSPolicyFaultDomainMoniker name="/AzureDAV/AzureDAVGroup/WebDAVFaultDomains" />
          </sCSPolicy>
        </groupHascomponents>
      </components>
      <sCSPolicy>
        <sCSPolicyUpdateDomain name="WebDAVUpgradeDomains" defaultPolicy="[5,5,5]" />
        <sCSPolicyFaultDomain name="WebDAVFaultDomains" defaultPolicy="[2,2,2]" />
        <sCSPolicyID name="WebDAVInstances" defaultPolicy="[1,1,1]" />
      </sCSPolicy>
    </group>
  </groups>
  <implements>
    <implementation Id="1e24e65a-eae6-4765-9e64-2b7a4089927b" ref="Microsoft.RedDog.Contract\ServiceContract\AzureDAVContract@ServiceDefinition">
      <interfacereferences>
        <interfaceReference Id="ce5083bc-ee48-4214-9f74-c68bdad86630" ref="Microsoft.RedDog.Contract\Interface\WebDAV:Endpoint1@ServiceDefinition">
          <inPort>
            <inPortMoniker name="/AzureDAV/AzureDAVGroup/WebDAV:Endpoint1" />
          </inPort>
        </interfaceReference>
      </interfacereferences>
    </implementation>
  </implements>
</serviceModel>
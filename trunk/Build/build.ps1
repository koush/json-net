﻿properties { 
  $zipFileName = "Json35r7.zip"
  $signAssemblies = $false
  $signKeyPath = "D:\Development\Releases\newtonsoft.snk"
  $buildDocumentation = $false
  
  $baseDir  = resolve-path ..
  $buildDir = "$baseDir\Build"
  $sourceDir = "$baseDir\Src"
  $toolsDir = "$baseDir\Tools"
  $releaseDir = "$baseDir\Release"
  $workingDir = "$baseDir\Working"
  $builds = @(
    @{Name = "Newtonsoft.Json.Silverlight"; TestsName = "Newtonsoft.Json.Tests.Silverlight"; Constants="SILVERLIGHT"; FinalDir="Silverlight"},
    @{Name = "Newtonsoft.Json.Compact"; TestsName = "Newtonsoft.Json.Tests.Compact"; Constants="PocketPC"; FinalDir="Compact"},
    @{Name = "Newtonsoft.Json.Net20"; TestsName = "Newtonsoft.Json.Tests.Net20"; Constants="NET20"; FinalDir="DotNet20"},
    @{Name = "Newtonsoft.Json"; TestsName = "Newtonsoft.Json.Tests"; Constants=""; FinalDir="DotNet"}
  )
} 

task default -depends Test

task Clean {
  Set-Location $baseDir
  
  if (Test-Path -path $workingDir)
  {
    Write-Output "Deleting Working Directory"
    
    del $workingDir -Recurse -Force
  }
  
  New-Item -Path $workingDir -ItemType Directory
}

task Build -depends Clean { 
  
  foreach ($build in $builds)
  {
    $name = $build.Name
    $finalDir = $build.FinalDir

    Write-Host -ForegroundColor Green "Building " $name
    Write-Host
    exec { msbuild "/t:Clean;Rebuild" /p:Configuration=Release /p:OutputPath=bin\Release\$finalDir\ /p:AssemblyOriginatorKeyFile=$signKeyPath "/p:SignAssembly=$signAssemblies" (GetConstants $build.Constants $signAssemblies) ".\Src\$name.sln" } "Error building $name"
  }
}

task Merge -depends Build {
  $binaryDir = "$sourceDir\Newtonsoft.Json\bin\Release\DotNet20"
  MergeAssembly "$binaryDir\Newtonsoft.Json.Net20.dll" $signKeyPath "$binaryDir\LinqBridge.dll"
  del $binaryDir\LinqBridge.dll

  $binaryDir = "$sourceDir\Newtonsoft.Json.Tests\bin\Release\DotNet20"
  MergeAssembly "$binaryDir\Newtonsoft.Json.Net20.dll" $signKeyPath "$binaryDir\LinqBridge.dll"
  MergeAssembly "$binaryDir\Newtonsoft.Json.Tests.Net20.dll" $signKeyPath "$binaryDir\LinqBridge.dll"
  del $binaryDir\LinqBridge.dll
}

task Package -depends Merge {
  foreach ($build in $builds)
  {
    $name = $build.TestsName
    $finalDir = $build.FinalDir
    
    Copy-Item -Path "$sourceDir\Newtonsoft.Json\bin\Release\$finalDir" -Destination $workingDir\Package\Bin\$finalDir -recurse
  }
  
  if ($buildDocumentation)
  {
    exec { msbuild "/t:Clean;Rebuild" /p:Configuration=Release .\Doc\doc.shfbproj } "Error building documentation. Check that you have Sandcastle, Sandcastle Help File Builder and HTML Help Workshop installed."
    
    New-Item -Path $workingDir\Package\Documentation -ItemType Directory
    move -Path $workingDir\Documentation\Documentation.chm -Destination $workingDir\Package\Documentation\Documentation.chm
    move -Path $workingDir\Documentation\LastBuild.log -Destination $workingDir\Documentation.log
  }

  robocopy $sourceDir $workingDir\Package\Source\Src /MIR /NP /XD .svn bin obj /XF *.suo *.user
  robocopy $buildDir $workingDir\Package\Source\Build /MIR /NP /XD .svn
  robocopy $docDir $workingDir\Package\Source\Doc /MIR /NP /XD .svn
  robocopy $toolsDir $workingDir\Package\Source\Tools /MIR /NP /XD .svn
  
  exec { .\Tools\7-zip\7za.exe a -tzip $workingDir\$zipFileName $workingDir\Package\* } "Error zipping"
}

task Deploy -depends Package {
  exec { .\Tools\7-zip\7za.exe x -y "-o$workingDir\Deployed" $workingDir\$zipFileName } "Error unzipping"
}

task Test -depends Deploy {
  foreach ($build in $builds)
  {
    $name = $build.TestsName
    $finalDir = $build.FinalDir
    
    Write-Host -ForegroundColor Green "Copying test assembly $name to deployed directory"
    Write-Host
    robocopy ".\Src\Newtonsoft.Json.Tests\bin\Release\$finalDir" $workingDir\Deployed\Bin\$finalDir /NP /XO /XF LinqBridge.dll
    
    Copy-Item -Path ".\Src\Newtonsoft.Json.Tests\bin\Release\$finalDir\$name.dll" -Destination $workingDir\Deployed\Bin\$finalDir\

    Write-Host -ForegroundColor Green "Running tests " $name
    Write-Host
    exec { .\Tools\NUnit\nunit-console.exe "$workingDir\Deployed\Bin\$finalDir\$name.dll" /xml:$workingDir\$name.xml } "Error running $name tests"
  }
}

function MergeAssembly($dllPrimaryAssembly, $signKey, [string[]]$mergedAssemlies)
{
  $mergeAssemblyPaths = [String]::Join(" ", $mergedAssemlies)
  
  $primary = Get-Item $dllPrimaryAssembly
  $mergedAssemblyName = $primary.Name
  $temporaryDir = $primary.DirectoryName + "\" + [Guid]::NewGuid().ToString()
  New-Item $temporaryDir -ItemType Directory
  
  try
  {
    exec { .\Tools\ILMerge\ilmerge.exe "/internalize" $ilMergeKeyFile "/out:$temporaryDir\$mergedAssemblyName" $dllPrimaryAssembly $mergeAssemblyPaths } "Error executing ILMerge"
    Copy-Item -Path $temporaryDir\$mergedAssemblyName -Destination $dllPrimaryAssembly -Force
  }
  finally
  {
    Remove-Item $temporaryDir -Recurse -Force
  }
}

function GetConstants($constants, $includeSigned)
{
  $signed = switch($includeSigned) { $true { ";SIGNED" } default { "" } }

  return "/p:DefineConstants=`"TRACE;$constants$signed`""
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

namespace SmartCodeGenerator.Engine
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                Console.WriteLine(((Exception) eventArgs.ExceptionObject).ToString());
            };

            var progressReporter = new ProgressReporter();

            var options =  OptionsHelper.LoadOptions(args, progressReporter);
            if (options == null)
            {
                return -1;
            }

            var selectedMsBuildInstance = MsBuildHelper.GetMsBuildInstance(progressReporter);
            if (selectedMsBuildInstance == null)
            {
                return -1;
            }

            MSBuildLocator.RegisterInstance(selectedMsBuildInstance);
            using (var workspace = MsBuildHelper.CreateMsBuildWorkspace(progressReporter))
            {
                var project = await workspace.OpenProjectAsync(options.ProjectPath, progressReporter);
                var generatorAssemblySearchPaths = options.GetGeneratorPluginsSearchPaths(progressReporter);
                var generator = new CompilationGenerator(generatorAssemblySearchPaths, options.OutputPath, progressReporter);
                await generator.Process(project);
            }
            return 0;
        }
    }
}

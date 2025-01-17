﻿using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MadsKristensen.AddAnyFile
{
	internal static class TemplateMap
	{
		private static readonly string _folder;
		private static readonly List<string> _templateFiles = new List<string>();
		private const string _defaultExt = ".txt";
		private const string _templateDir = ".templates";

		static TemplateMap()
		{
			var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var userProfile = Path.Combine(folder, ".vs", _templateDir);

			if (Directory.Exists(userProfile))
			{
				_templateFiles.AddRange(Directory.GetFiles(userProfile, "*" + _defaultExt, SearchOption.AllDirectories));
			}

			var assembly = Assembly.GetExecutingAssembly().Location;
			_folder = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
			_templateFiles.AddRange(Directory.GetFiles(_folder, "*" + _defaultExt, SearchOption.AllDirectories));
		}

		public static async Task<string> GetTemplateFilePathAsync(Project project, string file)
		{
			var extension = Path.GetExtension(file).ToLowerInvariant();
			var name = Path.GetFileName(file);
			var safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
			var relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");

			string templateFile = null;
			var list = _templateFiles.ToList();

			AddTemplatesFromCurrentFolder(list, Path.GetDirectoryName(file));

			// Look for direct file name matches
			if (list.Any(f => Path.GetFileName(f).Equals(name + _defaultExt, StringComparison.OrdinalIgnoreCase)))
			{
				var tmplFile = list.FirstOrDefault(f => Path.GetFileName(f).Equals(name + _defaultExt, StringComparison.OrdinalIgnoreCase));
				templateFile = Path.Combine(Path.GetDirectoryName(tmplFile), name + _defaultExt);//GetTemplate(name);
			}

			// Look for file extension matches
			else if (list.Any(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase)))
			{
				var tmplFile = list.FirstOrDefault(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
				var tmpl = AdjustForSpecific(safeName, extension);
				templateFile = Path.Combine(Path.GetDirectoryName(tmplFile), tmpl + _defaultExt); //GetTemplate(tmpl);
			}

			var template = await ReplaceTokensAsync(project, safeName, relative, templateFile);
			return NormalizeLineEndings(template);
		}

		private static void AddTemplatesFromCurrentFolder(List<string> list, string dir)
		{
			var current = new DirectoryInfo(dir);
			var dynaList = new List<string>();

			while (current != null)
			{
				var tmplDir = Path.Combine(current.FullName, _templateDir);

				if (Directory.Exists(tmplDir))
				{
					dynaList.AddRange(Directory.GetFiles(tmplDir, "*" + _defaultExt, SearchOption.AllDirectories));
				}

				current = current.Parent;
			}

			list.InsertRange(0, dynaList);
		}

		private static async Task<string> ReplaceTokensAsync(Project project, string name, string relative, string templateFile)
		{
			if (string.IsNullOrEmpty(templateFile))
			{
				return templateFile;
			}

			var rootNs = project.GetRootNamespace();
			var ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;

			if (!string.IsNullOrEmpty(relative))
			{
				ns += "." + ProjectHelpers.CleanNameSpace(relative);
			}

			var requestTypeName = string.Empty;

			if (ns.Contains("Application.Handlers."))
			{
				if (ns.Contains(".Queries."))
				{
					requestTypeName = "Query";
				}
				else if (ns.Contains(".Commands."))
				{
					requestTypeName = "Command";
				}
			}

			var handlerBaseNamespace = string.Empty;
			var apiVersionAttribute = string.Empty;

			if (ns.Contains(".Api.Controllers.") && (name.EndsWith("Controller") || name.Contains("Controller.")))
			{
				var dotIndex = name.IndexOf(".", StringComparison.Ordinal);
				if (dotIndex > 0)
				{
					name = name.Substring(0, dotIndex);
				}

				handlerBaseNamespace = $"{ns.Replace(".Api.Controllers.", ".Application.Handlers.")}.{name.Replace("Controller", string.Empty)}";
				handlerBaseNamespace = handlerBaseNamespace.Replace(".V2.", ".");

				if (dotIndex == -1 && ns.EndsWith(".Api.Controllers.V2"))
				{
					apiVersionAttribute = "\n[ApiVersion(\"2.0\")]";
				}
			}

			using (var reader = new StreamReader(templateFile))
			{
				var content = await reader.ReadToEndAsync();

				return content.Replace("{namespace}", ns)
							  .Replace("{itemname}", name)
							  .Replace("{requesttypename}", requestTypeName)
							  .Replace("{handlerbasenamespace}", handlerBaseNamespace)
							  .Replace("{apiversionattribute}", apiVersionAttribute);
			}
		}

		private static string NormalizeLineEndings(string content)
		{
			if (string.IsNullOrEmpty(content))
			{
				return content;
			}

			return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
		}

		private static string AdjustForSpecific(string safeName, string extension)
		{
			if (Regex.IsMatch(safeName, "^I[A-Z].*"))
			{
				return extension += "-interface";
			}

			if (safeName.EndsWith("Controller", StringComparison.Ordinal))
			{
				return extension += "-controller";
			}

			if (IsPartialController(safeName))
			{
				return extension += "-controller-partial";
			}

			return extension;
		}

		private static bool IsPartialController(string safeName)
		{
			return Regex.IsMatch(safeName, "Controller\\.[A-Z].*");
		}
	}
}

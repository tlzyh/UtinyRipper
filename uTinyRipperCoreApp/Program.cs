using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using uTinyRipper.Exporters;
using uTinyRipper.Converters;

using Object = uTinyRipper.Classes.Object;
using Version = uTinyRipper.Version;

namespace uTinyRipper
{
	class Program
	{
		public Program(string inputPath, string outputPath)
		{
			m_inputPath = inputPath;
			m_outputPath = outputPath;
		}

		public static bool AssetSelector(Object asset)
		{
			return true;
		}

		public bool Parse()
		{
			if(m_inputPath == null || !Directory.Exists(m_inputPath))
			{
				Console.WriteLine("无效输入文件夹");
				return false;
			}

			string[] files =
			{
				m_inputPath
			};

			ProcessInputFiles(files);
			return true;
		}

		public bool Export()
		{
			if (m_outputPath == null || !Directory.Exists(m_outputPath))
			{
				Console.WriteLine("无效输出文件夹");
				return false;
			}

			string path = Path.Combine(m_outputPath, GameStructure.Name);
			if (File.Exists(path))
			{
				Console.WriteLine("输出目标文件已存在：" + path);
				return false;
			}

			if (Directory.Exists(path))
			{
				if (Directory.EnumerateFiles(path).Any())
				{
					Console.WriteLine("输出文件夹已经存在：" + path);
					return false;
				}
			}

			Console.WriteLine("Exporting assets...");

			// ThreadPool.QueueUserWorkItem(new WaitCallback(ExportFiles), path);
			ExportFiles(path);

			return true;
		}

		private bool ProcessInputFiles(string[] files)
		{
			if (files.Length == 0)
			{
				return false;
			}

			foreach (string file in files)
			{
				if (MultiFileStream.Exists(file))
				{
					continue;
				}
				if (DirectoryUtils.Exists(file))
				{
					continue;
				}
				Logger.Log(LogType.Warning, LogCategory.General, MultiFileStream.IsMultiFile(file) ?
					$"File '{file}' doesn't has all parts for combining" :
					$"Neither file nor directory with path '{file}' exists");
				return false;
			}

			Console.WriteLine("Loading files...");
			m_processingFiles = files;
			// ThreadPool.QueueUserWorkItem(new WaitCallback(LoadFiles), files);
			LoadFiles(files);
			return true;
		}

		private void LoadFiles(object data)
		{
			string[] files = (string[])data;
			GameStructure = GameStructure.Load(files);
			if (GameStructure.IsValid)
			{
				Validate();
			}

			if (GameStructure.IsValid)
			{
				Console.WriteLine("Files has been loaded");
			}
			else
			{
				Logger.Log(LogType.Warning, LogCategory.Import, "Game files wasn't found");
			}
		}

		private void ExportFiles(object data)
		{
			m_exportPath = (string)data;
			PrepareExportDirectory(m_exportPath);

			TextureAssetExporter textureExporter = new TextureAssetExporter();
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Texture2D, textureExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Cubemap, textureExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Sprite, textureExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Shader, new ShaderAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.TextAsset, new TextAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.AudioClip, new AudioAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Font, new FontAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.MovieTexture, new MovieTextureAssetExporter());

			EngineAssetExporter engineExporter = new EngineAssetExporter();
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Material, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Texture2D, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Mesh, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Shader, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Font, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Sprite, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.MonoBehaviour, engineExporter);

			GameStructure.Export(m_exportPath, AssetSelector);
			Logger.Log(LogType.Info, LogCategory.General, "Finished!!!");

		}

		private void PrepareExportDirectory(string path)
		{
			string directory = Directory.GetCurrentDirectory();
			if (!PermissionValidator.CheckAccess(directory))
			{
				string arguments = string.Join(" ", m_processingFiles.Select(t => $"\"{t}\""));
				PermissionValidator.RestartAsAdministrator(arguments);
			}

			if (DirectoryUtils.Exists(path))
			{
				DirectoryUtils.Delete(path, true);
			}
		}

		private void Validate()
		{
			Version[] versions = GameStructure.FileCollection.GameFiles.Values.Select(t => t.Version).Distinct().ToArray();
			if (versions.Length > 1)
			{
				Logger.Log(LogType.Warning, LogCategory.Import, $"Asset collection has versions probably incompatible with each other. Here they are:");
				foreach (Version version in versions)
				{
					Logger.Log(LogType.Warning, LogCategory.Import, version.ToString());
				}
			}
		}


		private void OnExportFinished()
		{
			Console.WriteLine("export finished");
		}

		private void OnExportProgressUpdated(int index, int count)
		{
			double progress = ((double)index / count) * 100.0;
			Console.WriteLine($"exporting... {index}/{count} - {progress:0.00}%");
		}

		private void OnExportStarted()
		{
			Console.WriteLine("exporting...");
		}

		private void OnExportPreparationFinished()
		{
			Console.WriteLine("analysis finished");
		}

		private void OnExportPreparationStarted()
		{
			Console.WriteLine("analyzing assets...");
		}


		private GameStructure GameStructure
		{
			get => m_gameStructure;
			set
			{
				if (m_gameStructure == value)
				{
					return;
				}
				if (m_gameStructure != null && m_gameStructure.IsValid)
				{
					m_gameStructure.FileCollection.Exporter.EventExportFinished -= OnExportFinished;
					m_gameStructure.FileCollection.Exporter.EventExportProgressUpdated -= OnExportProgressUpdated;
					m_gameStructure.FileCollection.Exporter.EventExportStarted -= OnExportStarted;
					m_gameStructure.FileCollection.Exporter.EventExportPreparationFinished -= OnExportPreparationFinished;
					m_gameStructure.FileCollection.Exporter.EventExportPreparationStarted -= OnExportPreparationStarted;
				}
				m_gameStructure = value;
				if (value != null && value.IsValid)
				{
					value.FileCollection.Exporter.EventExportPreparationStarted += OnExportPreparationStarted;
					value.FileCollection.Exporter.EventExportPreparationFinished += OnExportPreparationFinished;
					value.FileCollection.Exporter.EventExportStarted += OnExportStarted;
					value.FileCollection.Exporter.EventExportProgressUpdated += OnExportProgressUpdated;
					value.FileCollection.Exporter.EventExportFinished += OnExportFinished;
				}
			}
		}

		private GameStructure m_gameStructure;
		private string m_exportPath;
		private string[] m_processingFiles;
		private string m_inputPath;
		private string m_outputPath;


		public static void Main(string[] args)
		{
			
		}

	}
}

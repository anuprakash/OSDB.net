﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using OSDBnet.Backend;
using ICSharpCode.SharpZipLib.GZip;

namespace OSDBnet {
	public class AnonymousClient : IAnonymousClient, IDisposable {

		private bool disposed = false;
		private const string USERAGENT = "OS Test User Agent";

		protected readonly IOsdb proxy;
		protected string token;

		internal AnonymousClient(IOsdb proxy) {
			this.proxy = proxy;
		}

		internal void Login(string language) {
			LoginResponse response = proxy.Login(string.Empty, string.Empty, language, USERAGENT);
			VerifyResponseCode(response);
			token = response.token;
		}

		public IEnumerable<Subtitle> SearchSubtitles(string filename) {
			if (string.IsNullOrEmpty(filename)) {
				throw new ArgumentNullException("filename");
			}
			FileInfo file = new FileInfo(filename);
			if (!file.Exists) {
				throw new ArgumentException("File doesn't exist", "filename");
			}
			var request = new SearchSubtitlesRequest { sublanguageid = "por,pob" };
			request.moviehash = HashHelper.ToHexadecimal(HashHelper.ComputeMovieHash(filename));
			request.moviebytesize = file.Length.ToString();

			request.imdbid = string.Empty;
			request.query = string.Empty;

			var response = proxy.SearchSubtitles(token, new SearchSubtitlesRequest[] { request });
			VerifyResponseCode(response);

			var subtitles = new List<Subtitle>();
			foreach (var info in response.data) {
				subtitles.Add(BuildSubtitleObject(info));
			}
			return subtitles;
		}

		public string DownloadSubtitleToPath(string path, Subtitle subtitle) {
			if (string.IsNullOrEmpty(path)) {
				throw new ArgumentNullException("path");
			}
			if (null == subtitle) {
				throw new ArgumentNullException("subtitle");
			}
			if (!Directory.Exists(path)) {
				throw new ArgumentException("path should point to a valid location");
			}

			string destinationfile = Path.Combine(path, subtitle.SubtitleFileName);
			string tempZipName = Path.GetTempFileName();
			try {
				WebClient webClient = new WebClient();
				webClient.DownloadFile(subtitle.SubTitleDownloadLink, tempZipName);

				UnZipSubtitleFileToFile(tempZipName, destinationfile);

			} finally {
				File.Delete(tempZipName);
			}

			return destinationfile;
		}

		public void  Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) {
			if (!disposed) {
				if (disposing && !string.IsNullOrEmpty(token)) {
					try {
						proxy.Logout(token);
					} catch {
						//soak it. We don't want exception on disposing. It's better to let the session timeout.
					}
					token = null;
				}
				disposed = true;
			}
		}

		~AnonymousClient() {
			Dispose(false);
		}

		protected static void UnZipSubtitleFileToFile(string zipFileName, string subFileName) {
			using (FileStream subFile = File.OpenWrite(subFileName))
			using (FileStream tempFile = File.OpenRead(zipFileName)) {
				var gzip = new GZipInputStream(tempFile);
				var buffer = new byte[4096];
				var bufferSize = 0;
				var readCount = 0;

				do {
					bufferSize = gzip.Read(buffer, readCount, buffer.Length);
					if (bufferSize > 0) {
						subFile.Write(buffer, readCount, bufferSize);
					}
				} while (bufferSize > 0);
				gzip.Dispose();
			}
		}

		protected static Subtitle BuildSubtitleObject(SearchSubtitlesInfo info) {
			var sub = new Subtitle {
				SubtitleId = info.IDSubtitle,
				SubtitleHash = info.SubHash,
				SubtitleFileName = info.SubFileName,
				SubTitleDownloadLink = new Uri(info.SubDownloadLink),
				SubtitlePageLink = new Uri(info.SubtitlesLink),
				LanguageId = info.SubLanguageID,
				LanguageName = info.LanguageName,

				ImdbId = info.IDMovieImdb,
				MovieId = info.IDMovie,
				MovieName = info.MovieName,
				OriginalMovieName = info.MovieNameEng,
				MovieYear = int.Parse(info.MovieYear)
			};
			return sub;
		}

		protected static void VerifyResponseCode(ResponseBase response) {
			if (null == response) {
				throw new ArgumentNullException("response");
			}
			if (string.IsNullOrEmpty(response.status)) {
				throw new ArgumentException("parameter response.status cannot be null", "response");
			}

			int responseCode = int.Parse(response.status.Substring(0,3));
			if (responseCode >= 400) {
				//TODO: Create Exception type
				throw new Exception(string.Format("Unexpected error response {1}", responseCode, response.status));
			}
		}
	}
}

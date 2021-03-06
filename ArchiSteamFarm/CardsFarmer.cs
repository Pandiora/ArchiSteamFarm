﻿/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using HtmlAgilityPack;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal class CardsFarmer {
		private const byte StatusCheckSleep = 5; // In minutes, how long to wait before checking the appID again

		private readonly Bot Bot;
		private bool NowFarming;
		private readonly AutoResetEvent AutoResetEvent = new AutoResetEvent(false);

		internal CardsFarmer(Bot bot) {
			Bot = bot;
		}

		internal async Task StartFarming() {
			Logging.LogGenericInfo(Bot.BotName, "Checking badges...");
			// Find the number of badge pages
			HtmlDocument badgesDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (badgesDocument == null) {
				Logging.LogGenericWarning(Bot.BotName, "Could not get badges information, farming is stopped!");
				return;
			}

			var maxPages = 1;
			HtmlNodeCollection badgesPagesNodeCollection = badgesDocument.DocumentNode.SelectNodes("//a[@class='pagelink']");
			if (badgesPagesNodeCollection != null) {
				maxPages = (badgesPagesNodeCollection.Count / 2) + 1; // Don't do this at home
			}

			// Find APPIDs we need to farm
			List<uint> appIDs = new List<uint>();
			for (var page = 1; page <= maxPages; page++) {
				Logging.LogGenericInfo(Bot.BotName, "Checking page: " + page + "/" + maxPages);

				if (page > 1) { // Because we fetched page number 1 already
					badgesDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);
					if (badgesDocument == null) {
						break;
					}
				}

				HtmlNodeCollection badgesPageNodes = badgesDocument.DocumentNode.SelectNodes("//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (badgesPageNodes == null) {
					continue;
				}

				foreach (HtmlNode badgesPageNode in badgesPageNodes) {
					string steamLink = badgesPageNode.GetAttributeValue("href", null);
					if (steamLink == null) {
						Logging.LogGenericWarning(Bot.BotName, "Couldn't get steamLink for one of the games: " + badgesPageNode.OuterHtml);
						continue;
					}

					uint appID = (uint) Utilities.OnlyNumbers(steamLink);
					if (appID == 0) {
						Logging.LogGenericWarning(Bot.BotName, "Couldn't get appID for one of the games: " + badgesPageNode.OuterHtml);
						continue;
					}

					if (Bot.Blacklist.Contains(appID)) {
						continue;
					}

					appIDs.Add(appID);
				}
			}

			// Start farming
			while (appIDs.Count > 0) {
				Logging.LogGenericInfo(Bot.BotName, "Farming in progress...");
				uint appID = appIDs[0];
				if (await Farm(appID).ConfigureAwait(false)) {
					appIDs.Remove(appID);
				} else {
					break;
				}
			}

			Logging.LogGenericInfo(Bot.BotName, "Farming finished!");
		}

		private async Task<bool?> ShouldFarm(ulong appID) {
			bool? result = null;
			HtmlDocument gamePageDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
			if (gamePageDocument != null) {
				HtmlNode gamePageNode = gamePageDocument.DocumentNode.SelectSingleNode("//span[@class='progress_info_bold']");
				if (gamePageNode != null) {
					result = !gamePageNode.InnerText.Contains("No card drops");
				}
			}
			return result;
		}

		private async Task<bool> Farm(ulong appID) {
			if (NowFarming) {
				AutoResetEvent.Set();
				Thread.Sleep(1000);
				AutoResetEvent.Reset();
			}

			bool success = true;
			bool? keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			while (keepFarming == null || keepFarming.Value) {
				if (!NowFarming) {
					NowFarming = true;
					Logging.LogGenericInfo(Bot.BotName, "Now farming: " + appID);
					Bot.PlayGame(appID);
				}
				if (AutoResetEvent.WaitOne(1000 * 60 * StatusCheckSleep)) {
					success = false;
					break;
				}
				keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			}

			Logging.LogGenericInfo(Bot.BotName, "Stopped farming: " + appID);
			Bot.PlayGame(0);
			NowFarming = false;
			return success;
		}
	}
}

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
using SteamKit2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal class ArchiWebHandler {
		private const int Timeout = 1000 * 15; // In miliseconds
		private readonly Bot Bot;
		private readonly string ApiKey;

		private ulong SteamID;
		private string VanityURL;
		private readonly Dictionary<string, string> SteamCookieDictionary = new Dictionary<string, string>();

		// This is required because home_process request must be done on final URL
		private string GetHomeProcess() {
			if (!string.IsNullOrEmpty(VanityURL)) {
				return "http://steamcommunity.com/id/" + VanityURL + "/home_process";
			} else if (SteamID != 0) {
				return "http://steamcommunity.com/profiles/" + SteamID + "/home_process";
			} else {
				return null;
			}
		}

		internal ArchiWebHandler(Bot bot, string apiKey) {
			Bot = bot;

			if (!string.IsNullOrEmpty(apiKey) && !apiKey.Equals("null")) {
				ApiKey = apiKey;
			}
		}

		internal async Task Init(SteamClient steamClient, string webAPIUserNonce, string vanityURL, string parentalPin) {
			if (steamClient == null || steamClient.SteamID == null || string.IsNullOrEmpty(webAPIUserNonce)) {
				return;
			}

			SteamID = steamClient.SteamID;
			VanityURL = vanityURL;

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(SteamID.ToString(CultureInfo.InvariantCulture)));

			// Generate an AES session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] cryptedSessionKey = null;
			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(steamClient.ConnectedUniverse))) {
				cryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Copy our login key
			byte[] loginKey = new byte[20];
			Array.Copy(Encoding.ASCII.GetBytes(webAPIUserNonce), loginKey, webAPIUserNonce.Length);

			// AES encrypt the loginkey with our session key
			byte[] cryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Send the magic
			KeyValue authResult;
			Logging.LogGenericInfo(Bot.BotName, "Logging in to ISteamUserAuth...");
			using (dynamic iSteamUserAuth = WebAPI.GetInterface("ISteamUserAuth")) {
				iSteamUserAuth.Timeout = Timeout;

				try {
					authResult = iSteamUserAuth.AuthenticateUser(
						steamid: SteamID,
						sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
						encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
						method: WebRequestMethods.Http.Post,
						secure: true
					);
				} catch (Exception e) {
					Logging.LogGenericException(Bot.BotName, e);
					steamClient.Disconnect(); // We may get 403 if we use the same webAPIUserNonce twice
					return;
				}
			}

			if (authResult == null) {
				steamClient.Disconnect(); // Try again
				return;
			}

			Logging.LogGenericInfo(Bot.BotName, "Success!");

			string steamLogin = authResult["token"].AsString();
			string steamLoginSecure = authResult["tokensecure"].AsString();

			SteamCookieDictionary.Clear();
			SteamCookieDictionary.Add("sessionid", sessionID);
			SteamCookieDictionary.Add("steamLogin", steamLogin);
			SteamCookieDictionary.Add("steamLoginSecure", steamLoginSecure);
			SteamCookieDictionary.Add("birthtime", "-473356799"); // ( ͡° ͜ʖ ͡°)

			if (!string.IsNullOrEmpty(parentalPin) && !parentalPin.Equals("0")) {
				Logging.LogGenericInfo(Bot.BotName, "Unlocking parental account...");
				Dictionary<string, string> postData = new Dictionary<string, string>() {
					{"pin", parentalPin}
				};

				HttpResponseMessage response = await Utilities.UrlPostRequestWithResponse("https://steamcommunity.com/parental/ajaxunlock", postData, SteamCookieDictionary, "https://steamcommunity.com/").ConfigureAwait(false);
				if (response != null && response.IsSuccessStatusCode) {
					Logging.LogGenericInfo(Bot.BotName, "Success!");

					var setCookieValues = response.Headers.GetValues("Set-Cookie");
					foreach (string setCookieValue in setCookieValues) {
						if (setCookieValue.Contains("steamparental=")) {
							string setCookie = setCookieValue.Substring(setCookieValue.IndexOf("steamparental=") + 14);
							setCookie = setCookie.Substring(0, setCookie.IndexOf(';'));
							SteamCookieDictionary.Add("steamparental", setCookie);
							break;
						}
					}
				} else {
					Logging.LogGenericInfo(Bot.BotName, "Failed!");
				}
			}

			Bot.Trading.CheckTrades();
		}

		internal List<SteamTradeOffer> GetTradeOffers() {
			if (ApiKey == null) {
				return null;
			}

			KeyValue response;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService")) {
				// Timeout
				iEconService.Timeout = Timeout;

				try {
					response = iEconService.GetTradeOffers(
						key: ApiKey,
						get_received_offers: 1,
						active_only: 1,
						secure: true
					);
				} catch (Exception e) {
					Logging.LogGenericException(Bot.BotName, e);
					return null;
				}
			}

			if (response == null) {
				return null;
			}

			List<SteamTradeOffer> result = new List<SteamTradeOffer>();
			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				SteamTradeOffer tradeOffer = new SteamTradeOffer {
					tradeofferid = trade["tradeofferid"].AsString(),
					accountid_other = trade["accountid_other"].AsInteger(),
					message = trade["message"].AsString(),
					expiration_time = trade["expiration_time"].AsInteger(),
					trade_offer_state = (SteamTradeOffer.ETradeOfferState) trade["trade_offer_state"].AsInteger(),
					items_to_give = new List<SteamItem>(),
					items_to_receive = new List<SteamItem>(),
					is_our_offer = trade["is_our_offer"].AsBoolean(),
					time_created = trade["time_created"].AsInteger(),
					time_updated = trade["time_updated"].AsInteger(),
					from_real_time_trade = trade["from_real_time_trade"].AsBoolean()
				};
				foreach (KeyValue item in trade["items_to_give"].Children) {
					tradeOffer.items_to_give.Add(new SteamItem {
						appid = item["appid"].AsString(),
						contextid = item["contextid"].AsString(),
						assetid = item["assetid"].AsString(),
						currencyid = item["currencyid"].AsString(),
						classid = item["classid"].AsString(),
						instanceid = item["instanceid"].AsString(),
						amount = item["amount"].AsString(),
						missing = item["missing"].AsBoolean()
					});
				}
				foreach (KeyValue item in trade["items_to_receive"].Children) {
					tradeOffer.items_to_receive.Add(new SteamItem {
						appid = item["appid"].AsString(),
						contextid = item["contextid"].AsString(),
						assetid = item["assetid"].AsString(),
						currencyid = item["currencyid"].AsString(),
						classid = item["classid"].AsString(),
						instanceid = item["instanceid"].AsString(),
						amount = item["amount"].AsString(),
						missing = item["missing"].AsBoolean()
					});
				}
				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task JoinClan(ulong clanID) {
			if (clanID == 0) {
				return;
			}

			string sessionID;
			if (!SteamCookieDictionary.TryGetValue("sessionid", out sessionID)) {
				return;
			}

			string request = "http://steamcommunity.com/gid/" + clanID;

			Dictionary<string, string> postData = new Dictionary<string, string>() {
				{"sessionID", sessionID},
				{"action", "join"}
			};

			await Utilities.UrlPostRequest(request, postData, SteamCookieDictionary).ConfigureAwait(false);
		}

		internal async Task LeaveClan(ulong clanID) {
			if (clanID == 0) {
				return;
			}

			string sessionID;
			if (!SteamCookieDictionary.TryGetValue("sessionid", out sessionID)) {
				return;
			}

			string request = GetHomeProcess();
			Dictionary<string, string> postData = new Dictionary<string, string>() {
				{"sessionID", sessionID},
				{"action", "leaveGroup"},
				{"groupId", clanID.ToString()}
			};
			await Utilities.UrlPostRequest(request, postData, SteamCookieDictionary).ConfigureAwait(false);
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				return false;
			}

			string sessionID;
			if (!SteamCookieDictionary.TryGetValue("sessionid", out sessionID)) {
				return false;
			}

			string referer = "https://steamcommunity.com/tradeoffer/" + tradeID + "/";
			string request = referer + "accept";

			Dictionary<string, string> postData = new Dictionary<string, string>() {
				{"sessionid", sessionID},
				{"serverid", "1"},
				{"tradeofferid", tradeID.ToString()}
			};

			HttpResponseMessage result = await Utilities.UrlPostRequestWithResponse(request, postData, SteamCookieDictionary, referer).ConfigureAwait(false);
			bool success = result.IsSuccessStatusCode;

			if (!success) {
				Logging.LogGenericWarning(Bot.BotName, "Request failed, reason: " + result.ReasonPhrase);
				switch (result.StatusCode) {
					case HttpStatusCode.InternalServerError:
						Logging.LogGenericWarning(Bot.BotName, "That might be caused by 7-days trade lock from new device");
						Logging.LogGenericWarning(Bot.BotName, "Try again in 7 days, declining that offer for now");
						DeclineTradeOffer(tradeID);
						break;
				}
			}

			return success;
		}

		internal bool DeclineTradeOffer(ulong tradeID) {
			if (ApiKey == null) {
				return false;
			}

			if (tradeID == 0) {
				return false;
			}

			KeyValue response;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService")) {
				// Timeout
				iEconService.Timeout = Timeout;

				try {
					response = iEconService.DeclineTradeOffer(
						key: ApiKey,
						tradeofferid: tradeID.ToString(),
						method: WebRequestMethods.Http.Post,
						secure: true
					);
				} catch (Exception e) {
					Logging.LogGenericException(Bot.BotName, e);
					return false;
				}
			}

			return response != null; // Steam API doesn't respond with any error code, assume any response is a success
		}

		internal async Task<HtmlDocument> GetBadgePage(int page) {
			if (SteamID == 0 || page == 0) {
				return null;
			}

			return await Utilities.UrlToHtmlDocument("http://steamcommunity.com/profiles/" + SteamID + "/badges?p=" + page, SteamCookieDictionary).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (SteamID == 0 || appID == 0) {
				return null;
			}

			return await Utilities.UrlToHtmlDocument("http://steamcommunity.com/profiles/" + SteamID + "/gamecards/" + appID, SteamCookieDictionary).ConfigureAwait(false);
		}
	}
}

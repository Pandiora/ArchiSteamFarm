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

using SteamKit2;
using SteamKit2.Internal;
using System.IO;

namespace ArchiSteamFarm {
	/// <summary>
	/// Message used to Accept or Decline a group(clan) invite.
	/// </summary>
	internal sealed class CMsgClientClanInviteAction : ISteamSerializableMessage, ISteamSerializable {
		EMsg ISteamSerializableMessage.GetEMsg() {
			return EMsg.ClientAcknowledgeClanInvite;
		}

		public CMsgClientClanInviteAction() {
		}

		/// <summary>
		/// Group invited to.
		/// </summary>
		internal ulong GroupID = 0;

		/// <summary>
		/// To accept or decline the invite.
		/// </summary>
		internal bool AcceptInvite = true;

		void ISteamSerializable.Serialize(Stream stream) {
			try {
				BinaryWriter binaryWriter = new BinaryWriter(stream);
				binaryWriter.Write(GroupID);
				binaryWriter.Write(AcceptInvite);
			} catch {
				throw new IOException();
			}
		}

		void ISteamSerializable.Deserialize(Stream stream) {
			try {
				BinaryReader binaryReader = new BinaryReader(stream);
				GroupID = binaryReader.ReadUInt64();
				AcceptInvite = binaryReader.ReadBoolean();
			} catch {
				throw new IOException();
			}
		}
	}
}

﻿using Discord;
using Discord.WebSocket;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Web;

namespace Dx2_DiscordBot
{
    /// <summary>
    /// This class is responsible for refreshing our apps data on an interval and posting a message to Discord afterwards
    /// </summary>
    public class GKRetriever : RetrieverBase
    {
        #region Properties
        
        //Our Timer Object
        public Timer Timer;

        //Used for POST
        private static readonly HttpClient client = new HttpClient();

        //List of our Factions
        private List<Faction> Factions = new List<Faction>();
        
        #endregion

        #region Constructor

        /// <summary>
        /// Creates our Timer and executes it
        /// </summary>
        public GKRetriever(DiscordSocketClient client) : base (client)
        {
            MainCommand = "!gk";

            //Calculate how much time until next hour mark is
            //After that update it will follow an update rate of updating x minutes after the hour and then each interval window after that
            var hour = DateTime.Now.Hour + 1;
            var day = 1;

            if (hour >= 24)
            {
                hour = hour - 24;
                day = day + 1;
            }

            var futureTime = new DateTime(1, 1, day, hour, Convert.ToInt32(ConfigurationManager.AppSettings["updateTime"]), 0);
            var currentTime = new DateTime(1, 1, 1, DateTime.Now.Hour, DateTime.Now.Minute, 0);
            var interval = futureTime.Subtract(currentTime).TotalMilliseconds;
            Logger.LogAsync("Time Until Next Update: " + interval);
            
            Timer = new Timer(interval);
            Timer.Elapsed += OnTimedEvent;
            Timer.AutoReset = true;
            Timer.Enabled = true;
        }

        #endregion

        #region Overrides

        //Initialization
        public async override Task ReadyAsync()
        {
            await GatherTopAsync();
        }

        //Recieve Messages here
        public async override Task MessageReceivedAsync(SocketMessage message, string serverName, ulong channelId)
        {
            //Let Base Update
            await base.MessageReceivedAsync(message, serverName, channelId);

            //Returns top ##
            if (message.Content.StartsWith("!gktop"))
            {
                var items = message.Content.Split("!gktop");

                //Try and Parse out Top number
                var top = -1;
                if (int.TryParse(items[1], out top))
                {
                    //Ensure no one can request anything but the numbers we wanted them too
                    top = Math.Clamp(top, 1, Convert.ToInt32(ConfigurationManager.AppSettings["topAmount"]));
                    await PostRankingsAsync(top, channelId, serverName);
                }
                else if (_client.GetChannel(channelId) is IMessageChannel chnl)
                    await chnl.SendMessageAsync("Could not understand: " + items[1]);
            }

            //Returns only your Faction
            if (message.Content == "!gkmyfaction")
                await PostMyFactionAsync(channelId, serverName);

            //Returns only your Faction
            if (message.Content.StartsWith("!gkbyname"))
            {
                var items = message.Content.Split("!gkbyname ");
                await PostMyFactionAsync(channelId, items[1]);
            }
        }

        //Returns the commands for this Retriever
        public override string GetCommands()
        {
            return "\n\nGatekeeper Event Commands: These commands will only work when a Gatekeeper event is going on." +
            "\n* !gkmyfaction - Displays your Faction's Rank and Damage. Faction Name is derived from your Discord Server Name.. make sure its 100% the same as your in-game name." +
            "\n* !gkbyname [Faction Name] - Gets a faction by its name allowing you to type your faction name in and get it back instead of using Discord Server name. Replace [Faction Name] with your factions exact name." +
            "\n* !gktop### - Displays a list of the top damage Factions up to the top 150 (ex: !gktop10, !gktop25, !gktop50, etc.)";
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods

        //Writes an amount of the top rankings
        private async Task PostRankingsAsync(int topAmount, ulong id, string factionName)
        {
            var chnl = _client.GetChannel(id) as IMessageChannel;

            var message = "";
            var maxAmount = Convert.ToInt32(ConfigurationManager.AppSettings["topAmount"]);
            if (topAmount > maxAmount)
                topAmount = maxAmount;

            //Ensures we can always have enough factions to loop through
            topAmount = Math.Min(topAmount, Factions.Count);

            for (var i = 0; i < topAmount; i++)
            {
                var f = Factions[i];
                message += f.Rank + " | " + f.Name + " | " + f.Damage + "\n";
            }

            //Only send a message when we have data
            if (message == "")
                message = "Couldn't find any factions something must be wrong please contact Alenael.";

            if (chnl != null)
            {
                await chnl.SendMessageAsync("```md\n" + message + "```");
                await Logger.LogAsync(factionName + " Recieved: " + message);
            }
            else
                await Logger.LogAsync(factionName + " could not write to channel " + id + "\n" + message);
        }


        //Gets the top 
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task GatherTopAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var tempFactions = new List<Faction>();
            var factionsToGet = Convert.ToInt32(ConfigurationManager.AppSettings["topAmount"]);

            var factionName = "";
            while (tempFactions.Count <= factionsToGet)
            {
                var factions = GetFactions(factionName);
                if (factions == null)
                    break;

                foreach (var faction in factions)
                    if (!tempFactions.Contains(faction) && tempFactions.Count <= factionsToGet)
                        tempFactions.Add(faction);

                //Jump to last faction
                factionName = factions[factions.Count - 1].Name;
            }

            Factions = tempFactions;
        }

        //Writes your factions rank and damage
        private async Task PostMyFactionAsync(ulong id, string factionName)
        {
            var factions = GetFactions(factionName);
            var chnl = _client.GetChannel(id) as IMessageChannel;

            var message = "";
            if (factions != null)
            {
                foreach (var f in factions)
                {
                    if (f.Name != factionName) continue;
                    message += f.Rank + " | " + f.Name + " | " + f.Damage + "\n";
                    break;
                }
            }

            if (message == "")
            {
                message = "Could not locate Faction: " + factionName +
                    ". Does your Discord Server name match your Faction name? Here is a list of what I found:";

                if (factions != null)
                    message = factions.Aggregate(message, (current, f) => current + "\n" + f.Name);
            }

            if (chnl != null)
            {
                await chnl.SendMessageAsync("```md\n" + message + "```");
                await Logger.LogAsync(factionName + " Recieved: " + message);
            }
            else
                await Logger.LogAsync(factionName + " could not write to channel " + id + "\n" + message);
        }

        //On our timed event
        private void OnTimedEvent(object sender, EventArgs e)
        {
            //Fix timer to update if we change our time in app config
            if (Timer.Interval != Convert.ToInt32(ConfigurationManager.AppSettings["interval"]))
            {
                Timer.Enabled = false;
                Timer.Interval = Convert.ToInt32(ConfigurationManager.AppSettings["interval"]);
                Timer.Enabled = true;
            }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            GatherTopAsync();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        //Gets 10 factions back at a time based on name provided
        private List<Faction> GetFactions(string factionName = "")
        {
            var web = new HtmlWeb();
            var htmlDoc = web.Load("https://ad2r-sim.mobile.sega.jp/socialsv/webview/GuildEventRankingView.do" + CleanFactionName(factionName));
            return ReadRankings(htmlDoc);
        }

        //Cleans a Faction Name
        private static string CleanFactionName(string factionName)
        {
            var fixedFactionName = factionName;

            if (fixedFactionName == "") return fixedFactionName;

            //Fix any faction names passed that have spaces at beginnning or end of their names
            fixedFactionName = fixedFactionName.Trim();

            //Fix for factions with & symbol in there name to make them url safe
            fixedFactionName = HttpUtility.UrlEncode(fixedFactionName);

            //Completes the URL
            fixedFactionName = "?guild_name=" + fixedFactionName.Replace(" ", "+") + "&x=59&y=28&search_flg=1&lang=1";

            return fixedFactionName;
        }

        //Processes Rankings we pass to this and return them in a list
        private List<Faction> ReadRankings(HtmlDocument htmlDoc)
        {
            var factions = new List<Faction>();

            var otherNodes = htmlDoc.DocumentNode.SelectNodes("//tr");
            var damageNodes = htmlDoc.DocumentNode.SelectNodes("//p[@class='dmgStr']");

            if (otherNodes == null || damageNodes == null) return null;
                        
            for (var i = 0; i < damageNodes.Count; i++)
            {
                var rank = otherNodes[i + 1].ChildNodes[1].InnerText;
                var name = otherNodes[i + 1].ChildNodes[3].InnerText;
                var damage = damageNodes[i].InnerText;

                factions.Add(
                    new Faction()
                    {
                        Rank = rank,
                        Name = name,
                        Damage = damage
                    });
            }

            return factions;
        }

        #endregion
    }

    #region Structs

    // Small Struct to hold Faction Data
    public struct Faction
    {
        public string Rank;
        public string Name;
        public string Damage;
    }

    #endregion
}
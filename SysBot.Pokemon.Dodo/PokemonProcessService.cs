﻿using System;
using System.Net;
using System.Net.Http;
using DoDo.Open.Sdk.Models.Bots;
using DoDo.Open.Sdk.Models.Events;
using DoDo.Open.Sdk.Models.Messages;
using DoDo.Open.Sdk.Models.Personals;
using DoDo.Open.Sdk.Services;
using PKHeX.Core;
using SysBot.Base;

namespace SysBot.Pokemon.Dodo
{
    public class PokemonProcessService<TP> : EventProcessService where TP : PKM, new()
    {
        private readonly OpenApiService _openApiService;
        private static readonly string LogIdentity = "DodoBot";
        private static readonly string Welcome = "at我并尝试对我说：\n皮卡丘\ntrade ps代码\n或者直接拖一个文件进来";
        private readonly string _channelId;
        private string _botDodoSourceId;

        public PokemonProcessService(OpenApiService openApiService, string channelId)
        {
            _openApiService = openApiService;
            _channelId = channelId;
        }

        public override void Connected(string message)
        {
            Console.WriteLine($"{message}\n");
        }

        public override void Disconnected(string message)
        {
            Console.WriteLine($"{message}\n");
        }

        public override void Reconnected(string message)
        {
            Console.WriteLine($"{message}\n");
        }

        public override void Exception(string message)
        {
            Console.WriteLine($"{message}\n");
        }

        public override void PersonalMessageEvent<T>(
            EventSubjectOutput<EventSubjectDataBusiness<EventBodyPersonalMessage<T>>> input)
        {
            var eventBody = input.Data.EventBody;

            if (eventBody.MessageBody is MessageBodyText messageBodyText)
            {
                DodoBot<TP>.SendPersonalMessage(eventBody.DodoSourceId, $"你好", eventBody.IslandSourceId);
            }
        }

        public override void ChannelMessageEvent<T>(
            EventSubjectOutput<EventSubjectDataBusiness<EventBodyChannelMessage<T>>> input)
        {
            var eventBody = input.Data.EventBody;
            if (!string.IsNullOrWhiteSpace(_channelId) && eventBody.ChannelId != _channelId) return;

            if (eventBody.MessageBody is MessageBodyFile messageBodyFile)
            {
                if (!ValidFileSize(messageBodyFile.Size ?? 0) || !ValidFileName(messageBodyFile.Name))
                {
                    DodoBot<TP>.SendChannelMessage("非法文件", eventBody.ChannelId);
                    return;
                }
                using var client = new HttpClient();
                var downloadBytes = client.GetByteArrayAsync(messageBodyFile.Url).Result;
                var p = GetPKM(downloadBytes);
                if (p is TP pkm)
                {
                    DodoHelper<TP>.StartTrade(pkm, eventBody.DodoSourceId, eventBody.Personal.NickName, eventBody.ChannelId, eventBody.IslandSourceId);
                }

                return;
            }

            if (eventBody.MessageBody is not MessageBodyText messageBodyText) return;

            var content = messageBodyText.Content;

            LogUtil.LogInfo($"{eventBody.Personal.NickName}({eventBody.DodoSourceId}):{content}", LogIdentity);
            if (_botDodoSourceId == null)
            {
                _botDodoSourceId = _openApiService.GetBotInfo(new GetBotInfoInput()).DodoSourceId;
            }
            if (!content.Contains($"<@!{_botDodoSourceId}>")) return;

            content = content.Substring(content.IndexOf('>') + 1);
            if (content.Trim().StartsWith("trade"))
            {
                content = content.Replace("trade", "");
                DodoHelper<TP>.StartTrade(content, eventBody.DodoSourceId, eventBody.Personal.NickName, eventBody.ChannelId, eventBody.IslandSourceId);
                return;
            } 
            else if (content.Trim().StartsWith("dump"))
            {
                DodoHelper<TP>.StartDump(eventBody.DodoSourceId, eventBody.Personal.NickName, eventBody.ChannelId);
                return;
            }
            else if (typeof(TP) == typeof(PK9) && content.Trim().StartsWith("批量"))// 仅SV支持批量，其他偷懒还没写
            {
                DodoHelper<TP>.StartTradeMulti(content.Replace("批量", "").Trim(), eventBody.DodoSourceId, eventBody.Personal.NickName, eventBody.ChannelId, eventBody.IslandSourceId);
                return;
            }

            var ps = ShowdownTranslator<TP>.Chinese2Showdown(content);
            if (!string.IsNullOrWhiteSpace(ps))
            {
                LogUtil.LogInfo($"收到命令\n{ps}", LogIdentity);
                DodoHelper<TP>.StartTrade(ps, eventBody.DodoSourceId, eventBody.Personal.NickName, eventBody.ChannelId, eventBody.IslandSourceId);
            }
            else if (content.Contains("取消"))
            {
                var result = DodoBot<TP>.Info.ClearTrade(ulong.Parse(eventBody.DodoSourceId));
                DodoBot<TP>.SendChannelAtMessage(ulong.Parse(eventBody.DodoSourceId), $" {GetClearTradeMessage(result)}",
                    eventBody.ChannelId);
            }
            else if (content.Contains("位置"))
            {
                var result = DodoBot<TP>.Info.CheckPosition(ulong.Parse(eventBody.DodoSourceId));
                DodoBot<TP>.SendChannelAtMessage(ulong.Parse(eventBody.DodoSourceId),
                    $" {GetQueueCheckResultMessage(result)}",
                    eventBody.ChannelId);
            }
            else
            {
                DodoBot<TP>.SendChannelMessage($"{Welcome}", eventBody.ChannelId);
            }
        }

        public string GetQueueCheckResultMessage(QueueCheckResult<TP> result)
        {
            if (!result.InQueue || result.Detail is null)
                return "你不在队列里";
            var msg = $"你在第{result.Position}位";
            var pk = result.Detail.Trade.TradeData;
            if (pk.Species != 0)
                msg += $"，交换宝可梦：{ShowdownTranslator<TP>.GameStringsZh.Species[result.Detail.Trade.TradeData.Species]}";
            return msg;
        }

        private static string GetClearTradeMessage(QueueResultRemove result)
        {
            return result switch
            {
                QueueResultRemove.CurrentlyProcessing => "你正在交换中",
                QueueResultRemove.CurrentlyProcessingRemoved => "正在删除",
                QueueResultRemove.Removed => "已删除",
                _ => "你不在队列里",
            };
        }

        private static bool ValidFileSize(long size)
        {
            if (typeof(TP) == typeof(PK8) || typeof(TP) == typeof(PB8) || typeof(TP) == typeof(PK9))
            {
                return size == 344;
            }

            if (typeof(TP) == typeof(PA8))
            {
                return size == 376;
            }

            return false;
        }

        private static bool ValidFileName(string fileName)
        {
            return (typeof(TP) == typeof(PK8) && fileName.EndsWith("pk8", StringComparison.OrdinalIgnoreCase)
                    || typeof(TP) == typeof(PB8) && fileName.EndsWith("pb8", StringComparison.OrdinalIgnoreCase)
                    || typeof(TP) == typeof(PA8) && fileName.EndsWith("pa8", StringComparison.OrdinalIgnoreCase)
                    || typeof(TP) == typeof(PK9) && fileName.EndsWith("pk9", StringComparison.OrdinalIgnoreCase));
        }

        private static PKM GetPKM(byte[] bytes)
        {
            if (typeof(TP) == typeof(PK8)) return new PK8(bytes);
            if (typeof(TP) == typeof(PB8)) return new PB8(bytes);
            if (typeof(TP) == typeof(PA8)) return new PA8(bytes);
            if (typeof(TP) == typeof(PK9)) return new PK9(bytes);
            return null;
        }

        public override void MessageReactionEvent(
            EventSubjectOutput<EventSubjectDataBusiness<EventBodyMessageReaction>> input)
        {
            // Do nothing
        }
    }
}
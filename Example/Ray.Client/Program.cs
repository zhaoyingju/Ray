﻿using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Ray.Handler;
using Ray.IGrains;
using Ray.IGrains.Actors;
using Ray.RabbitMQ;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ray.Core;
using Ray.Core.Message;

namespace Ray.Client
{
    class Program
    {
        static int Main(string[] args)
        {
            return RunMainAsync().Result;
        }

        private static async Task<int> RunMainAsync()
        {
            try
            {
                using (var client = await StartClientWithRetries())
                {
                    Global.Init(client.ServiceProvider);
                    await HandlerStart.Start(new[] { "Core", "Read" }, client.ServiceProvider, client);
                    var aActor = client.GetGrain<IAccount>("1");
                    var bActor = client.GetGrain<IAccount>("2");
                    var aActorReplicated = client.GetGrain<IAccountReplicated>("1");
                    var bActorReplicated = client.GetGrain<IAccountReplicated>("2");
                    while (true)
                    {
                        Console.WriteLine("Press Enter to terminate...");
                        Console.ReadLine();
                        await aActor.AddAmount(1000);//1充值1000
                        await aActor.Transfer("2", 500);//转给2500
                        await Task.Delay(200);
                        var aBalance = await aActor.GetBalance();
                        var bBalance = await bActor.GetBalance();
                        Console.WriteLine($"1的余额为{aBalance},2的余额为{bBalance}");

                        var aBalanceReplicated = await aActorReplicated.GetBalance();
                        var bBalanceReplicated = await bActorReplicated.GetBalance();
                        Console.WriteLine($"1的副本余额为{aBalance},2的副本余额为{bBalance}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }

        private static async Task<IClusterClient> StartClientWithRetries(int initializeAttemptsBeforeFailing = 5)
        {
            int attempt = 0;
            IClusterClient client;
            while (true)
            {
                try
                {
                    var config = ClientConfiguration.LocalhostSilo();
                    client = new ClientBuilder()
                        .UseConfiguration(config)
                        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(IAccount).Assembly).WithReferences())
                        .ConfigureLogging(logging => logging.AddConsole())
                        .ConfigureServices((servicecollection) =>
                        {
                            servicecollection.AddSingleton<ISerializer, ProtobufSerializer>();//注册序列化组件
                            servicecollection.AddRabbitMQ<MessageInfo>();//注册RabbitMq为默认消息队列
                            servicecollection.PostConfigure<RabbitConfig>(c =>
                            {
                                c.UserName = "admin";
                                c.Password = "luohuazhiyu";
                                c.Hosts = new[] { "127.0.0.1:5672" };
                                c.MaxPoolSize = 100;
                                c.VirtualHost = "/";
                            });
                        })
                        .Build();

                    await client.Connect();
                    Console.WriteLine("Client successfully connect to silo host");
                    break;
                }
                catch (SiloUnavailableException)
                {
                    attempt++;
                    Console.WriteLine($"Attempt {attempt} of {initializeAttemptsBeforeFailing} failed to initialize the Orleans client.");
                    if (attempt > initializeAttemptsBeforeFailing)
                    {
                        throw;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(4));
                }
            }

            return client;
        }
    }
}

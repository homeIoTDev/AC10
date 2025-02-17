﻿using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Diagnostics;
using MQTTnet.Adapter;
using MQTTnet.Implementations;
using System.Text;
using System.Collections.Concurrent;


namespace HeatingDaemon;

public class MqttAdapter: IDisposable, IMqttService
{
    private readonly    ILogger<MqttAdapter>        _logger;
    private readonly    MqttAdapterConfig           _config;
    private readonly    MqttFactory                 _mqttFactory;
    private             MqttClientOptions?          _mqttClientOptions;
    private             System.Timers.Timer         _openConnectionTimer;
    private             IMqttClient?                _mqttClient;
    private readonly    CancellationTokenSource     _cts = new CancellationTokenSource();
    private             ConcurrentQueue<(string ReadingName, string Value)>
                                                    _sendingQueue = new ConcurrentQueue<(string ReadingName, string Value)>();
    private             AutoResetEvent              _newSendingQueueElementEvent = new AutoResetEvent(false);
    private readonly    Dictionary<string, string>  _readings = new Dictionary<string, string>();


    public MqttAdapter(IOptions<MqttAdapterConfig> config, ILogger<MqttAdapter> logger)
    {
        _config         = config.Value;
        _logger         = logger;
        _mqttFactory    = new MQTTnet.MqttFactory(new MqttNetLogger(_logger));
        ConfigureMqtt();

        _openConnectionTimer = new System.Timers.Timer(5000);
        _logger.LogInformation("MqttAdapter initialized with configuration.");
    }

    private void ConfigureMqtt()
    {
        _mqttClientOptions = new MqttClientOptionsBuilder()
                                .WithConnectionUri(_config.ServerUri)
                                .WithClientId(_config.ClientId)
                                .WithoutThrowOnNonSuccessfulConnectResponse()
                                .Build();
        _mqttClient = _mqttFactory.CreateMqttClient();
    }
    
    public void Start()
    {
        _logger.LogInformation("Starting MQTT broker monitoring loop.");
        _openConnectionTimer.Elapsed += (sender, e) => OpenConnection();
        _openConnectionTimer.AutoReset    = true;
        _openConnectionTimer.Start();
        Task.Run(() => ProcessSendingQueue(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _logger.LogInformation("Stop MQTT broker monitoring loop and terminate existing connections.");
        _cts.Cancel();
        _openConnectionTimer?.Stop();
        _openConnectionTimer?.Dispose();
        if( _mqttClient?.IsConnected == true)
        {
            try
            {
                _logger.LogInformation("Disconnecting from MQTT broker...");
                _mqttClient.DisconnectAsync().Wait();
                _logger.LogInformation("Disconnected from MQTT broker.");   
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Disconnecting from MQTT broker failed.");
            }
        }
    }

    /// <summary>
    /// Setzt den Wert einer Heizungsvariable. Der Wert wird im MQTT-Adapter zwischengespeichert und
    /// erst dann an den MQTT-Broker gesendet, wenn sich der Wert aendert.
    /// </summary>
    /// <param name="name">Name der Heizungsvariable</param>
    /// <param name="value">Wert der Heizungsvariable</param>
    /// <param name="forceSend">Sende den Wert auch dann, wenn er sich nicht geändert hat.</param>
    public void SetReading(string name, string value, bool forceSend = false)
    {
        if (_readings.ContainsKey(name))
        {
            if (_readings[name] != value)
            {
                _readings[name] = value;
                SendReading(name, value);
            }
            else if (forceSend)
            {
                SendReading(name, value);
            }
        }
        else
        {
            _readings.Add(name, value);
            SendReading(name, value);
        }
    }
    public void LogAllReadings()
    {
        foreach (var item in _readings)
        {
           _logger.LogInformation($"Reading: {item.Key} = {item.Value}");
        }
        _logger.LogInformation($"Current Time: {DateTime.Now}");

    }

    private void SendReading(string readingName, string value)
    {
        _logger.LogTrace($"Enqueuing MQTT message to topic '{_config.Topic}/{readingName}' with payload '{value}'...");
        // Initialize the queue if not already done
        _sendingQueue.Enqueue((readingName, value));
        _newSendingQueueElementEvent.Set();
        return;
    }

    private void ProcessSendingQueue(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_mqttClient?.IsConnected == true && _sendingQueue.TryDequeue(out var tuble) )
            { 
                PublishMessage(tuble.ReadingName, tuble.Value);
            } 
            else
            { 
                // Warte, bis ein neues Element in die Queue eingefügt wird oder die zeit abgelaufen ist
                _newSendingQueueElementEvent.WaitOne(TimeSpan.FromSeconds(1)); 
            } 
        }
    }

    private void PublishMessage(string topic, string payload)
    {
        try
        {
            _logger.LogInformation($"Sending MQTT message to topic '{_config.Topic}/{topic}' with payload '{payload}'...");
            var message = new MqttApplicationMessageBuilder()
                            .WithTopic(_config.Topic + "/" + topic)
                            .WithPayload(payload)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build();
            _mqttClient?.PublishAsync(message, _cts.Token).Wait();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Sending MQTT message to topic '{_config.Topic}/{topic}' with payload '{payload}' failed.");
        }
    }

    private void OpenConnection()
    {
        if(_mqttClientOptions == null || _mqttClient == null || _mqttClient.IsConnected==true)
        {
            return;
        }

        try
        {
            var connectResult = _mqttClient.ConnectAsync(_mqttClientOptions).GetAwaiter().GetResult();
            if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation($"Successfully connected to MQTT Broker {_mqttClientOptions.ChannelOptions.ToString()}.");
            }
            else
            {
                _logger.LogError($"Could not connection to MQTT Brocker {_mqttClientOptions.ChannelOptions.ToString()}. ResultCode: {connectResult.ResultCode}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Could not connection to MQTT Brocker{_mqttClientOptions.ChannelOptions.ToString()}. Trying again in 5 seconds.");
            return;
        }

    }

    public void Dispose()
    {
        _logger.LogInformation("MqttAdapter disposed.");
        // Beende den Service
    }
}

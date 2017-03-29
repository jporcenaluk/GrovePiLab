using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using GrovePi;
using GrovePi.Sensors;
using GrovePi.I2CDevices;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using System.Text;

namespace GrovePiLab
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // This is a default place holder connection string for the lab. 
        // During Day 3 lab, you will replace the string with your own connection string once you 
        // have created your own IoT Hub and device on the hub.
        const string deviceConnectionString = "HostName=VirtualLabIotHub.azure-devices.net;DeviceId=6833085;SharedAccessKey=ku1xGWBust/Lc4bKvORQnAHklPCAtsrN/s830cGsvdY=";


        public MainPage()
        {
            this.InitializeComponent();
            InitializeGrove();
            InitDeviceClient();
            StartTelemetry();
            InitAzureIotReceiver();
        }

        IRgbLcdDisplay display;
        IDHTTemperatureAndHumiditySensor sensor;
        bool lcdIsGreen = true; // initial state

        private void InitializeGrove()
        {
            try
            {
                string groveVersion = DeviceFactory.Build.GrovePi().GetFirmwareVersion();
                Debug.WriteLine(groveVersion);
                sensor = DeviceFactory.Build.DHTTemperatureAndHumiditySensor(Pin.DigitalPin4, DHTModel.Dht11);
                display = DeviceFactory.Build.RgbLcdDisplay();

                //Initialize LCD to Green
                SetLcdGreen(true);
                SetLcdText("You did it bro.");
                GroveStatus.Text = "Grove gone done and initted.";
            }
            catch (Exception e)
            {
                Debug.WriteLine("Grove not found :" + e.ToString());
                GroveStatus.Text = "Grove not found.";
                display = null;
                sensor = null;
            }
        }

        private SolidColorBrush redBrush = new SolidColorBrush(Windows.UI.Colors.Red);
        private SolidColorBrush greenBrush = new SolidColorBrush(Windows.UI.Colors.Green);

        private void SetLcdText(string text)
        {
            if (display != null)
            {
                display.SetText(text);
            }
        }

        private void SetLcdGreen(bool isGreen)
        {
            LCD.Fill = isGreen ? greenBrush : redBrush;
            StateText.Text = isGreen ? "Green" : "Red";
            if (display != null)
            {
                if (isGreen)
                {
                    display.SetBacklightRgb(0, 255, 0);
                }
                else
                {
                    display.SetBacklightRgb(255, 0, 0);
                }
            }
        }

        private void FlipButton_Click(object sender, RoutedEventArgs e)
        {
            lcdIsGreen = !lcdIsGreen;
            SetLcdGreen(lcdIsGreen);
        }

        private class TelemetryData
        {
            public string DeviceId;
            public string LcdColor;
            public double Temperature;
            public double Humidity;
            public double WindSpeed;
            public bool LiveData;

            public TelemetryData()
            {
                //Make up some default random data for Azure lab if there is no live data
                Random random = new Random();
                DeviceId = "JaredPorcenalukRPi3a";
                LcdColor = "green";
                LiveData = false;

                //Some random number with +/- ranges
                Temperature = Math.Ceiling((90 + random.NextDouble() * 4 - 2) * 10) / 10;
                Humidity = Math.Ceiling((70 + random.NextDouble() * 4 - 2) * 10) / 10;
                WindSpeed = Math.Ceiling((60 + random.NextDouble() * 4 - 2) * 10) / 10;
            }
        }

        private TelemetryData MeasureTelemetry()
        {
            TelemetryData data = new TelemetryData();
            data.LcdColor = lcdIsGreen ? "green" : "red";
            if (sensor != null)
            {
                try
                {
                    sensor.Measure();
                    data.Temperature = sensor.TemperatureInFahrenheit;
                    data.Humidity = sensor.Humidity;
                    data.LiveData = true;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.ToString());
                }
            }
            return data;
        }

        private void StartTelemetry()
        {
            Task.Run(async () =>
            {
                int msgCount = 0;
                while (true)
                {
                    TelemetryData data = MeasureTelemetry();
                    string lcdText = string.Format("Temp = {0:##.#}       Humidity = {1}%", data.Temperature.ToString(), data.Humidity.ToString());

                    // Output to LCD on UI thread
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
                    {
                        SetLcdGreen(lcdIsGreen);
                        SetLcdText(lcdText);
                    });

                    string msgString = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                    Debug.WriteLine(msgString);

                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
                    {
                        GroveStatus.Text = String.Format("[{0}] Last Telemetry: {1}", msgCount++, msgString);
                    });

                    await SendDeviceToCloudMessageAsync(msgString);

                    //Wait a second between every measurement
                    Task.Delay(1000).Wait();
                }
            });
        }

        DeviceClient deviceClient;
        public void InitDeviceClient()
        {
            deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Http1);

        }

        public async Task SendDeviceToCloudMessageAsync(string messageText)
        {
            try
            {
                var message = new Message(Encoding.ASCII.GetBytes(messageText));
                await deviceClient.SendEventAsync(message);

                Debug.WriteLine("Sent message " + messageText);
            }
            catch (Exception e)
            {
                Debug.WriteLine("SendDeviceToCloudMessageAsync error = : " + e.ToString());
            }
        }

        public async Task<string> ReceiveCloudToDeviceMessageAsync()
        {
            while (true)
            {
                try
                {
                    var receiveMessage = await deviceClient.ReceiveAsync();
                    if (receiveMessage != null)
                    {
                        var messageData = Encoding.ASCII.GetString(receiveMessage.GetBytes());
                        await deviceClient.CompleteAsync(receiveMessage);
                        return messageData;
                    }

                }
                catch (Exception e)
                {
                    Debug.WriteLine("Error in ReceiveCloudToDeviceMessageAsync: " + e.ToString());
                }

                await Task.Delay(1000);
            }
        }

        private void InitAzureIotReceiver()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    string receivedMessage = await ReceiveCloudToDeviceMessageAsync();
                    if (receivedMessage == null) continue;
                    Debug.WriteLine("Received message = " + receivedMessage);

                    switch (receivedMessage.ToLower())
                    {
                        case "green":
                            {
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
                                {
                                    lcdIsGreen = true;
                                    SetLcdGreen(lcdIsGreen);
                                });
                                break;
                            }
                        case "red":
                            {
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
                                {
                                    lcdIsGreen = false;
                                    SetLcdGreen(lcdIsGreen);
                                });
                                break;
                            }
                        default: { break; }
                    }
                }
            });
        }
    }
}

using System.Collections.Concurrent;
using System.Numerics;
using System.IO;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.Json;


namespace UnityAudioRecorder
{
    public class RecordingHandler
    {
        static RecordingHandler instance;
        public RecordingHandler()
        {
            if (instance == null)
            {
                instance = this;
                string filePath = Application.persistentDataPath + "/AudioRecording/";
                System.IO.Directory.CreateDirectory(filePath);
            }
            else
            {
                throw new System.OperationCanceledException("Only one instance can be created.");
            }
        }
        public int MaxLength = 60;

        public float coroutineInterval = 0.2f;

        public GameObject recordPos;

        ///<summary>
        ///default is 8000
        ///</summary>
        public int recordingFrequency
        {
            get { return micInUse.usedFreq; }
            set { micInUse.usedFreq = value; }
        }

        private ConcurrentDictionary<string, transformJson> fileIndex;
        public List<transformJson> fileList
        {
            get
            {
                return fileIndex.Select(x => { return x.Value; }).ToList();
            }
        }
        public Action<List<transformJson>> onGetFilesFinish;
        private List<MicDevice> MicrophoneList;//manager level
        private MicDevice micInUse;//manager level

        private AudioClip recording; //manager level
        private int currentPos;//audio clip last position after consumed, unity level, multiple changeable during one recording, manager level
        private long length_file = 44;//only one for one audio recording,recording level
        private FileStream file_str;//recording level
        private string currentFileName;
        private MemoryStream memory_str;//recording level
        private bool isRecording = false;

        public bool deleteFile(string fileName)
        {
            transformJson temp;
            if (fileIndex.TryGetValue(fileName, out temp))
            {
                File.Delete(temp.realPath);
                fileIndex.TryRemove(fileName, out _);
                return true;
            }
            else
            {
                return false;
            }
            //File.Delete(fileName);
        }

        public bool renameFile(string fileName, string targetName)
        {
            transformJson temp;
            if (fileIndex.ContainsKey(targetName))
            {
                return false;
            }
            if (fileIndex.TryGetValue(fileName, out temp))
            {
                temp.fileName = targetName;
                var tt = Path.Combine(Path.GetDirectoryName(temp.realPath), targetName);
                fileIndex.TryRemove(fileName, out _);
                fileIndex.TryAdd(targetName, temp);
                File.Move(temp.realPath, tt);
                temp.realPath = tt;
                return true;
            }
            else
            {
                return false;
            }
        }
        public void updatePos(string fileName, GameObject obj)
        {
            transformJson temp;
            if (fileIndex.TryGetValue(fileName, out temp))
            {
                temp.pos = obj.transform.localPosition;
                temp.rot = obj.transform.localEulerAngles;
                temp.scl = obj.transform.localScale;
                switch (temp.isValid)
                {
                    case true:
                        try
                        {
                            using (FileStream stream = new FileStream(temp.realPath, FileMode.Open))
                            {
                                stream.Seek(-36, SeekOrigin.End);
                                additionalPos(stream, obj);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.Log(e.Message);
                        }
                        break;
                    case false:
                        try
                        {
                            using (FileStream stream = new FileStream(temp.realPath, FileMode.Open))
                            {
                                stream.Seek(0, SeekOrigin.End);
                                additionalPos(stream, obj);
                                temp.isValid = true;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.Log(e.Message);
                        }
                        break;
                }
            }

        }

        // private void fromFile(string filePath)
        // {
        //     using (StreamReader reader = new StreamReader(filePath))
        //     {
        //         JsonSerializer.Deserialize<List<transformJson>>(reader.ReadToEnd());
        //     }
        // }

        public IEnumerator getRecordingFiles()
        {
            string filePath = Application.persistentDataPath + "/AudioRecording/";
            var temp = Directory.GetFiles(filePath, "*.wav");
            fileIndex = new ConcurrentDictionary<string, transformJson>();
            foreach (var i in temp)
            {
                yield return new WaitForSecondsRealtime(0.0167f);
                //Debug.Log(Path.GetFileName(i));
                //lock (fileIndex) 
                //Debug.Log(Time.realtimeSinceStartup);
                fileIndex.TryAdd(Path.GetFileName(i), useFile(i));
                //Debug.Log(Time.realtimeSinceStartup);
            }
            yield return null;
            onGetFilesFinish.Invoke(fileList);
            //StartCoroutine(playAudioRecord2(temp[0]));
        }

        private transformJson useFile(string fileName)
        {
            transformJson temp = new transformJson(fileName);
            temp.fileName = Path.GetFileName(fileName);
            using (FileStream stream = new FileStream(fileName, FileMode.Open))
            {
                stream.Seek(4, SeekOrigin.Begin);
                var filesize = new byte[4];
                stream.Read(filesize, 0, 4);
                if (stream.Length > BitConverter.ToUInt32(filesize, 0) + 8)
                {
                    stream.Seek(-36, SeekOrigin.End);
                    var byteTemp = new byte[4];
                    stream.Read(byteTemp, 0, 4);
                    temp.pos.x = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.pos.y = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.pos.z = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.rot.x = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.rot.y = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.rot.z = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.scl.x = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.scl.y = BitConverter.ToSingle(byteTemp, 0);
                    stream.Read(byteTemp, 0, 4);
                    temp.scl.z = BitConverter.ToSingle(byteTemp, 0);
                }
                else
                {
                    temp.isValid = false;
                }
            }
            return temp;
        }
        public IEnumerator playAudioRecord(string temp, GameObject audioObj)
        {
            transformJson tt;
            yield return null;
            if (!fileIndex.TryGetValue(temp, out tt))
            {
                yield break;
            }
            using (var m = UnityWebRequestMultimedia.GetAudioClip("file://" + tt.realPath, AudioType.WAV))
            {
                yield return m.SendWebRequest();
                audioObj.GetComponent<AudioSource>().clip = DownloadHandlerAudioClip.GetContent(m);
                audioObj.GetComponent<AudioSource>().Play();
            }
        }

        public void RefreshMic()
        {
            micInUse = new MicDevice();//create new micdevice to use in this recording session
            UpdateMicrophoneList();//refresh the microphone hardware list and fill the micInUse with first device specifications by default
            //micInUse.usedFreq = 8000;//(micInUse.MaxFreq + micInUse.MinFreq) / 2;
        }
        public void startRecording()
        {
            if (isRecording)
            {
                return;
            }
            isRecording = true;
            length_file = 44;
            useMic(micInUse.name);//stop if applicable the previous recording session and start a new one with the device name retrieved in previous step and selected by user
                                  //currentPos = 0;//set position to zero to record audio from the beginning
            ToFile();//create a file stream with current time and write to it the audio reocrding data
                     //recordingRoutine = 
                     //StartCoroutine(writeRecording());
        }

        public void writeRecording()
        {
            if (!isRecording)
            {
                return;
            }
            var cur_pos = Microphone.GetPosition(micInUse.name);
            var leftPos = recording.samples - cur_pos;//Time.deltaTime*recording.frequency*recording.channels

            var shiftPos = cur_pos - currentPos;
            //Debug.Log(leftPos);


            if (shiftPos > 64)
            {
                // var mem_pos_before = memory_str.Position;
                //var file_pos_before = file_str.Position;
                //int tunkSize = Mathf.CeilToInt(Time.deltaTime * recording.frequency);
                //var m = Microphone.GetPosition(micInUse.name);
                float[] temp = new float[(cur_pos - currentPos) * recording.channels];

                recording.GetData(temp, currentPos); currentPos = cur_pos;// += tunkSize;
                                                                          //float[] temp;
                                                                          // recording.GetData
                ConvertAndWrite(memory_str, temp, ref length_file);
                //Debug.Log(temp[0]);
                // var mem_pos_now = memory_str.Position;
                // memory_str.Seek(mem_pos_before - mem_pos_now, SeekOrigin.Current);
                //memory_str.CopyTo(file_str);
                // memory_str.Seek(mem_pos_now, SeekOrigin.Begin);
                //file_str.Seek(file_pos_before,SeekOrigin.Begin);
                //file_str.Seek(mem_pos_now-mem_pos_before, SeekOrigin.Current);
                if (leftPos < coroutineInterval * recording.frequency * recording.channels * 2)
                {
                    //micInUse.usedFreq = recording.frequency;
                    useMic(micInUse.name);
                    cur_pos = 0;
                }
            }
        }
        public void stopRecording()
        {
            if (!isRecording)
            {
                return;
            }
            isRecording = false;
            if (Microphone.IsRecording(micInUse.name))
            {
                Microphone.End(micInUse.name);
            }
            finalizeHeader(memory_str, length_file);
            additionalPos(memory_str, recordPos);
            memory_str.WriteTo(file_str);
            file_str.Dispose();
            memory_str.Dispose();
        }
        public void abortRecording()
        {
            if (!isRecording)
            {
                return;
            }
            isRecording = false;
            if (Microphone.IsRecording(micInUse.name))
            {
                Microphone.End(micInUse.name);
            }
            //finalizeHeader(memory_str, length_file);
            //additionalPos(memory_str,recordPos);
            //memory_str.WriteTo(file_str);
            file_str.Dispose();
            memory_str.Dispose();
            File.Delete(currentFileName);
        }

        private void additionalPos(Stream memoryStream, GameObject recordPos)
        {
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localPosition.x), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localPosition.y), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localPosition.z), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localEulerAngles.x), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localEulerAngles.y), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localEulerAngles.z), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localScale.x), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localScale.y), 0, 4);
            memoryStream.Write(BitConverter.GetBytes(recordPos.transform.localScale.z), 0, 4);
        }

        public void UpdateMicrophoneList()
        {
            //MicrophoneList = Microphone.devices;
            MicrophoneList = new List<MicDevice>();
            Microphone.devices.ToList().ForEach(device =>
            {
                var temp = new MicDevice();
                temp.name = device;
                Microphone.GetDeviceCaps(temp.name, out temp.MinFreq, out temp.MaxFreq);
                MicrophoneList.Add(temp);
            });
            micInUse = MicrophoneList[0];
#if UNITY_EDITOR && SHOW_DEBUG
            foreach (var i in MicrophoneList) { Debug.Log(i.name); }
#endif
        }
        private void setMic(string mic_name)
        {
            micInUse = MicrophoneList.Where(x => x.name == mic_name).First();
        }
        public void useMic(string deviceName)
        {
            try
            {
                if (Microphone.IsRecording(micInUse.name))
                {
                    Microphone.End(micInUse.name);
                }
                //micInUse.name = deviceName;
                micInUse = MicrophoneList.Where(x => x.name == deviceName).First();
                //gameObject.GetComponent<AudioSource>().clip = //recording;
                recording =
                Microphone.Start(micInUse.name, false, MaxLength, micInUse.usedFreq);
                //recording = gameObject.GetComponent<AudioSource>().clip;
#if UNITY_EDITOR && SHOW_DEBUG
                Debug.Log(micInUse.name + " is using frequency: " + micInUse.usedFreq);
#endif
                currentPos = 0;
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
        }
        private void ToFile()
        {
            string audio_suffix = "wav";
            string filePath = Application.persistentDataPath + "/AudioRecording/";
            string filePath_Complete = filePath + System.DateTime.UtcNow.ToString("yyyy-MM-dd_hh-mm-ss") + "." + audio_suffix;
            currentFileName = filePath_Complete;
#if UNITY_EDITOR && SHOW_DEBUG
            Debug.Log(Path.GetDirectoryName(filePath_Complete));
            Debug.Log(filePath_Complete);
#endif
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            file_str = new FileStream(filePath_Complete, FileMode.OpenOrCreate);
            memory_str = new MemoryStream();
            // var mem_pos_before = memory_str.Position;
            WriteHeader(memory_str, recording);
            // var mem_pos_now = memory_str.Position;
            // memory_str.Seek(mem_pos_before - mem_pos_now, SeekOrigin.Current);
            //memory_str.CopyTo(file_str, 44);
            // memory_str.Seek(mem_pos_now, SeekOrigin.Begin);
            //recording.
        }
        private class MicDevice
        {
            public int MaxFreq, MinFreq;
            public int usedFreq = 8000;
            public string name;
        }
        public class transformJson
        {
            public transformJson(string path)
            {
                realPath = path;
            }
            public string fileName;
            public string realPath;
            internal bool isValid = true;
            public UnityEngine.Vector3 pos = UnityEngine.Vector3.zero;
            public UnityEngine.Vector3 rot = UnityEngine.Vector3.zero;
            public UnityEngine.Vector3 scl = UnityEngine.Vector3.one;
        }
        static void ConvertAndWrite(MemoryStream fileStream, float[] samples, ref long length_file)
        {

            //var samples = clip;

            //clip.GetData(samples, 0);

            Int16[] intData = new Int16[samples.Length];
            //converting in 2 float[] steps to Int16[], //then Int16[] to Byte[]

            Byte[] bytesData = new Byte[samples.Length * 2];
            length_file += samples.Length * 2;
            //bytesData array is twice the size of
            //dataSource array because a float converted in Int16 is 2 bytes.

            float rescaleFactor = 32767; //to convert float to Int16

            for (int i = 0; i < samples.Length; i++)
            {
                intData[i] = (short)(samples[i] * rescaleFactor);
                Byte[] byteArr = new Byte[2];
                byteArr = BitConverter.GetBytes(intData[i]);
                byteArr.CopyTo(bytesData, i * 2);
            }

            fileStream.Write(bytesData, 0, bytesData.Length);
        }

        static void WriteHeader(MemoryStream fileStream, AudioClip clip)
        {

            var hz = clip.frequency;
            var channels = clip.channels;
            var samples = clip.samples;

            fileStream.Seek(0, SeekOrigin.Begin);

            Byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
            fileStream.Write(riff, 0, 4);

            Byte[] chunkSize = BitConverter.GetBytes(fileStream.Length - 8);
            fileStream.Write(chunkSize, 0, 4);

            Byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
            fileStream.Write(wave, 0, 4);

            Byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
            fileStream.Write(fmt, 0, 4);

            Byte[] subChunk1 = BitConverter.GetBytes(16);
            fileStream.Write(subChunk1, 0, 4);

            //UInt16 two = 2;
            UInt16 one = 1;

            Byte[] audioFormat = BitConverter.GetBytes(one);
            fileStream.Write(audioFormat, 0, 2);

            Byte[] numChannels = BitConverter.GetBytes(channels);
            fileStream.Write(numChannels, 0, 2);

            Byte[] sampleRate = BitConverter.GetBytes(hz);
            fileStream.Write(sampleRate, 0, 4);

            Byte[] byteRate = BitConverter.GetBytes(hz * channels * 2); // sampleRate * bytesPerSample*number of channels, here 44100*2*2
            fileStream.Write(byteRate, 0, 4);

            UInt16 blockAlign = (ushort)(channels * 2);
            fileStream.Write(BitConverter.GetBytes(blockAlign), 0, 2);

            UInt16 bps = 16;
            Byte[] bitsPerSample = BitConverter.GetBytes(bps);
            fileStream.Write(bitsPerSample, 0, 2);

            Byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
            fileStream.Write(datastring, 0, 4);

            Byte[] subChunk2 = BitConverter.GetBytes(samples * channels * 2);
            fileStream.Write(subChunk2, 0, 4);

            //		fileStream.Close();
        }
        static void finalizeHeader(Stream fileStream, long length_file)
        {
            var temp = fileStream.Position;//keep record of current position

            fileStream.Seek(4, SeekOrigin.Begin);
            //modify file size
            Byte[] filesize = BitConverter.GetBytes(length_file - 8);
            fileStream.Write(filesize, 0, 4);

            fileStream.Seek(40, SeekOrigin.Begin);
            //modify chunk size
            Byte[] chunkSize = BitConverter.GetBytes(length_file - 44);
            fileStream.Write(chunkSize, 0, 4);

            fileStream.Seek(temp, SeekOrigin.Begin);//go back to current position for whatever reason
        }
    }
}

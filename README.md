UnityAudioRecorder
==================
With GameObject position being recorded at the same time, later merged into some NLP project 

Extracted version of audio recording class relying on UnityEngine itself (and .net json library processing the information that may be useful in the future) which makes it cross-platform for Unity usages.

 - Create .wav audio in `Application.persistentDataPath+"/AudioRecording/"` folder 
 - Audio operation: start reocrding, stop recording, abort recording and playback recorded audio files
 - File operation: get recording files, delete file, rename file
 - Game Object operation: position in scene update
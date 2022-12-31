# nolife_norm_r128
Old tools used at Nolife to normalize audios in videos

I don't think this will be useful for anyone since it's basically quick & dirty frontends for ffmpeg & co, but here they are.

Those tools were used in the production chain of Nolife TV from 2013 to 2018. I now hold the whole copyright to them so I'm leaving them in GPLv3.

## Normalisation audio R128

A simple tool that take videos or audio (wav), analyze them and export either the wav or remux them in mov or mkv.

Simply drag & drop the files to analyze or normalize on the window and it will run the process automatically (audio demux, analysis, normalizing, remux if asked).

The audio normalization uses EBU R 128 loudness normalization standard used in TV broadcast and pro streaming. Basically the loudness should be -23 LUFS for TV (it's actually enforced) and -16 LUFS is recommended for online videos.  
LUFS is a loudness measurements that uses a "human perception" algorithm. Meaning if every video is at the same LUFS level, you actually will never have to adjust the volume since they will all sound at the same loudness level. It works the same way as the LKFS of the ITU-R BS.1770 norm which shares the same features.

A number of helpers are needed for it to work, that must be present in the same directory, namely ffmpeg.exe, sox, MediaInfo.exe and r128gain.exe. Since it was initially a tool used in broadcast production, they were all frozen to specific (tested and validated) versions with static builds. You can find working versions in the release archive, and compatibility is not guaranteed for newer versions.

A number of specificity applies, like if you ask for remuxing the video in mov, it will NOT recompress video if using some whitelisted codecs (like DV, Avid DNxHD, etc). This was done on purpose since the codecs were natively compatible with Avid Media Composer, which we used at the time. Video recompression was added later as convenient way to convert external videos (like mp4, vob etc) easily for editing.

MOV remux will leave the PCM audio uncompressed in all case.

MKV remux is a feature I just added, if selected it will recompress audio in AAC and keep the original video (no recompression).

Building needs Nolife.Diagnostics.dll that can also be found in the binary archive, but you can also safely discard this library which was used for debug purpose in production.

![image](https://user-images.githubusercontent.com/17545417/210124304-b2abc7fa-38e0-4b49-823c-5fef658b1b05.png)


## PasteMix

A not very interesting tool but here it is anyway :) it's a simple .exe on which you must drag a .mov, it will look for a matching wav in the same folder or in another remote folders (configurable) and will export a remuxed mov file.

It was actually used in our production chain where the audio mix was often done at the same time the video editing was being finished. So the video editor would export the finished video with unfinished audio, and the sound engineer would export the finished audio file in a specific folder. Then we only had to drop the video file on this tool, which then would find the final audio on the network, remux it and export the final folder in a folder to be queued for broadcast & SVOD compression :)


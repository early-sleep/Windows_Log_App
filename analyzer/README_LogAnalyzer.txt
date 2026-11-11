Log Analyzer  v1.1
======================================================================

1. What it is
----------------------------------------------------------------------
A local web page for reading the packages produced by Laptop Log Collector
and asking an AI model to analyse selected log files.

It runs entirely on your machine: a tiny local server serves the page and
reads the log files; only the text you select is sent to the AI endpoint
you configure.

2. How to run
----------------------------------------------------------------------
Double-click LogAnalyzer.exe.
It starts a local server on 127.0.0.1 and opens the page in your browser.
Keep the console window open while you use the page; close it to stop.

Optional: pass a folder as the first argument to change where it looks
for packages, for example

    LogAnalyzer.exe D:\test\packages

3. Finding packages
----------------------------------------------------------------------
By default it scans  Desktop\LogCollection  for sub-folders that contain
report.html or Collection_Info.txt (that is what the collector produces).

Type any other folder in the box at the top right and press "Scan".

4. Reading logs
----------------------------------------------------------------------
The left sidebar lists the package grouped by its 10_.., 20_.., 30_.. etc.
folders. Click a group to expand it.

  * text / html files  -> click the name to preview on the right
  * .evtx / .dmp       -> shown greyed out, they are binary and are not
                          sent to the model

5. Asking AI
----------------------------------------------------------------------
1) Open the "AI analysis" tab
2) Pick logs: "Core logs" selects the small but information-dense set
   (key events, power settings, disk health, hardware summary...);
   "Every text log" selects all text files
3) Pick a question or write your own
4) Press "Analyze with AI"

The answer can be copied or downloaded as a .md file.

6. AI settings
----------------------------------------------------------------------
Open the "Settings" tab and fill in:

  Endpoint  any OpenAI-compatible base URL
            DeepSeek   https://api.deepseek.com/v1
            OpenAI     https://api.openai.com/v1
            Ollama     http://localhost:11434/v1   (local, no key needed)
  API key   your key; it stays in this browser's local storage
  Model     e.g. deepseek-chat, gpt-4o-mini, qwen2.5:14b

Characters per file / total characters control how much text is sent,
which keeps the request inside the model's context window.

Nothing is uploaded to us; the selected text goes only to the endpoint
you typed in.

7. Limitations
----------------------------------------------------------------------
* .evtx and .dmp cannot be read by the page - analyse them with Event
  Viewer and WinDbg. The collector already exports key_events.csv,
  which the AI can read.
* Very large files are truncated (default 20,000 characters per file).
* The AI sees only what you selected; it cannot open other files.

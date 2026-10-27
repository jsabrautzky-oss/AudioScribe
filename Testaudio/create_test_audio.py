import asyncio
import edge_tts

DIALOG = [
    ("de-DE-ConradNeural", "Guten Tag zusammen. Willkommen zu unserer heutigen Besprechung über das neue Software-Release."),
    ("de-DE-KatjaNeural", "Hallo Markus. Ich habe mir den aktuellen Stand der Pipeline angesehen, die Tests laufen alle stabil durch."),
    ("de-DE-ConradNeural", "Das klingt hervorragend. Gibt es noch offene Punkte bezüglich der Hugging Face Authentifizierung?"),
    ("de-DE-KatjaNeural", "Nein, der Token wird sicher über die Windows DPAPI gespeichert und die Fehlerbehandlung greift wie gewünscht."),
    ("de-DE-ConradNeural", "Sehr gut. Dann können wir das Projekt jetzt final testen und exportieren.")
]

async def main():
    with open("test_meeting.mp3", "wb") as outfile:
        for voice, text in DIALOG:
            communicate = edge_tts.Communicate(text, voice)
            async for chunk in communicate.stream():
                if chunk["type"] == "audio":
                    outfile.write(chunk["data"])
            # Kurze Pause zwischen den Sprechern simulieren
            await asyncio.sleep(0.5)

asyncio.run(main())
print("Testdatei 'test_meeting.mp3' erfolgreich erstellt!")
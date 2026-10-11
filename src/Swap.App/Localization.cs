using System.Globalization;
using System.Windows;
namespace Swap.App;

public static class Localization
{
    public static readonly string[] Languages = ["es", "en", "pt", "fr", "de", "it"];
    public static string Language { get; private set; } = "en";
    // Each row has the key and all six translations. No partial-language fallback in the interface.
    private const string Catalog = """
backupCreate|Crear respaldo|Create backup|Criar backup|Créer une sauvegarde|Sicherung erstellen|Crea backup
backupSaved|Respaldo creado y verificado. Los drivers no se modificaron.|Backup created and verified. Drivers were not changed.|Backup criado e verificado. Os drivers não foram alterados.|Sauvegarde créée et vérifiée. Les pilotes restent inchangés.|Sicherung erstellt und geprüft. Treiber wurden nicht geändert.|Backup creato e verificato. I driver non sono stati modificati.
healthRecovery|Ver respaldos|View backups|Ver backups|Voir les sauvegardes|Sicherungen anzeigen|Apri backup
systemReboot|Windows tiene un reinicio pendiente. El diagnóstico está disponible; los cambios de driver quedan bloqueados.|Windows has a pending restart. Diagnostics are available; driver changes are blocked.|O Windows tem uma reinicialização pendente. O diagnóstico está disponível; alterações de driver estão bloqueadas.|Windows attend un redémarrage. Le diagnostic reste disponible ; les changements de pilote sont bloqués.|Windows wartet auf einen Neustart. Diagnose ist verfügbar; Treiberänderungen sind gesperrt.|Windows richiede un riavvio. La diagnostica è disponibile; le modifiche ai driver sono bloccate.
healthScope|Comprueba Windows y Direct3D; no certifica cables, alimentación ni juegos.|Checks Windows and Direct3D; does not certify cables, power or games.|Verifica Windows e Direct3D; não certifica cabos, alimentação ou jogos.|Vérifie Windows et Direct3D ; ne certifie ni câbles, ni alimentation, ni jeux.|Prüft Windows und Direct3D; bestätigt keine Kabel, Stromversorgung oder Spiele.|Verifica Windows e Direct3D; non certifica cavi, alimentazione o giochi.
healthView|Ver diagnóstico|View diagnostics|Ver diagnóstico|Voir le diagnostic|Diagnose anzeigen|Apri diagnostica
healthRefresh|Comprobar estado|Check status|Verificar estado|Vérifier l'état|Status prüfen|Verifica stato
unknown|Sin datos|Unknown|Sem dados|Inconnu|Unbekannt|Sconosciuto
maximumHz|máximo disponible|maximum available|máximo disponível|maximum disponible|maximal verfügbar|massimo disponibile
eventTitle|Errores gráficos recientes|Recent graphics errors|Erros gráficos recentes|Erreurs graphiques récentes|Letzte Grafikfehler|Errori grafici recenti
displayTitle|Pantallas y frecuencia|Displays and refresh rate|Telas e taxa de atualização|Écrans et fréquence|Displays und Bildwiederholrate|Schermi e frequenza
lastComparison|Comparación del último cambio|Last change comparison|Comparação da última alteração|Comparaison du dernier changement|Vergleich der letzten Änderung|Confronto ultima modifica
checkpointTitle|Cambios desde el punto de control|Changes since checkpoint|Alterações desde o ponto de controle|Changements depuis le point de contrôle|Änderungen seit Prüfpunkt|Modifiche dal punto di controllo
checkpointSave|Guardar punto de control|Save checkpoint|Salvar ponto de controle|Enregistrer le point de contrôle|Prüfpunkt speichern|Salva punto di controllo
checkpointConfirm|¿Usar el estado actual como nuevo punto de control? Sustituye la referencia anterior.|Use current state as checkpoint? Replaces the previous reference.|Usar o estado atual como ponto de controle? Substitui a referência anterior.|Utiliser l'état actuel comme point de contrôle ? Remplace la référence.|Aktuellen Zustand als Prüfpunkt verwenden? Ersetzt die vorherige Referenz.|Usare lo stato attuale come punto di controllo? Sostituisce il riferimento.
checkpointReadError|No se pudo guardar o leer el punto de control|Could not save or read checkpoint|Não foi possível salvar ou ler o ponto de controle|Impossible de lire ou enregistrer le point de contrôle|Prüfpunkt konnte nicht gelesen oder gespeichert werden|Impossibile leggere o salvare il punto di controllo
checkpointOtherMachine|Punto de control de otro equipo; no se compara.|Checkpoint belongs to another computer; not compared.|Ponto de controle de outro PC; não comparado.|Point de contrôle d'un autre PC ; non comparé.|Prüfpunkt eines anderen PCs; kein Vergleich.|Punto di controllo di un altro PC; non confrontato.
checkpointNone|Sin punto de control. Se guarda al verificar un driver válido.|No checkpoint. Saved when a valid driver is verified.|Sem ponto de controle. Salvo ao verificar um driver válido.|Aucun point de contrôle. Enregistré après vérification d'un pilote valide.|Kein Prüfpunkt. Wird bei gültigem Treiber gespeichert.|Nessun punto di controllo. Salvato con un driver valido.
checkpointUnchanged|Sin cambios respecto al punto de control|No changes since checkpoint|Sem alterações desde o ponto de controle|Aucun changement depuis le point de contrôle|Keine Änderungen seit Prüfpunkt|Nessuna modifica dal punto di controllo
newDisplayErrors|Errores gráficos posteriores al punto de control|Graphics errors after checkpoint|Erros gráficos após o ponto de controle|Erreurs graphiques après le point de contrôle|Grafikfehler seit Prüfpunkt|Errori grafici dopo il punto di controllo
displayRouteChanged|Cambió la GPU de salida|Display GPU routing changed|A GPU de saída mudou|GPU de sortie modifié|Display-GPU-Zuordnung geändert|GPU di uscita modificata
displayReduced|Resolución o Hz inferiores al punto de control|Resolution or Hz below checkpoint|Resolução ou Hz abaixo do ponto de controle|Résolution ou Hz inférieurs au point de contrôle|Auflösung oder Hz unter Prüfpunkt|Risoluzione o Hz inferiori al punto di controllo
displayRemoved|Pantalla ya no detectada|Display no longer detected|Tela não mais detectada|Écran désormais absent|Display nicht mehr erkannt|Schermo non più rilevato
driverRegressed|El driver perdió su estado válido|Driver lost its valid state|O driver perdeu seu estado válido|Le pilote a perdu son état valide|Treiber hat gültigen Zustand verloren|Il driver ha perso lo stato valido
driverChanged|El driver cambió desde el punto de control|Driver changed since checkpoint|O driver mudou desde o ponto de controle|Pilote modifié depuis le point de contrôle|Treiber seit Prüfpunkt geändert|Driver modificato dal punto di controllo
gpuAdded|Nueva GPU detectada|New GPU detected|Nova GPU detectada|Nouveau GPU détecté|Neue GPU erkannt|Nuova GPU rilevata
gpuRemoved|GPU ya no detectada|GPU no longer detected|GPU não mais detectada|GPU désormais absent|GPU nicht mehr erkannt|GPU non più rilevata
displayErrors|Errores gráficos registrados en 7 días|Graphics errors recorded in 7 days|Erros gráficos registrados em 7 dias|Erreurs graphiques enregistrées en 7 jours|Grafikfehler in 7 Tagen protokolliert|Errori grafici registrati in 7 giorni
noDisplayErrors|Sin errores gráficos registrados en 7 días|No recorded graphics errors in 7 days|Sem erros gráficos registrados em 7 dias|Aucune erreur graphique enregistrée en 7 jours|Keine Grafikfehler in 7 Tagen protokolliert|Nessun errore grafico registrato in 7 giorni
eventsUnknown|Registro de errores sin verificar|Error history not verified|Histórico de erros não verificado|Historique d'erreurs non vérifié|Fehlerverlauf nicht geprüft|Registro errori non verificato
refreshAvailable|Frecuencia superior disponible|Higher refresh rate available|Taxa de atualização maior disponível|Fréquence supérieure disponible|Höhere Bildwiederholrate verfügbar|Frequenza superiore disponibile
displayConnected|Pantallas activas en esta GPU|Active displays on this GPU|Telas ativas nesta GPU|Écrans actifs sur ce GPU|Aktive Displays an dieser GPU|Schermi attivi su questa GPU
noDirectDisplay|Sin pantalla directa; el equipo puede usar otra GPU|No direct display; routing may use another GPU|Sem tela direta; o PC pode usar outra GPU|Aucun écran direct ; une autre GPU peut être utilisée|Kein direktes Display; eine andere GPU kann verwendet werden|Nessuno schermo diretto; il PC può usare un'altra GPU
displayUnknown|Pantallas sin verificar|Displays not verified|Telas não verificadas|Écrans non vérifiés|Displays nicht geprüft|Schermi non verificati
direct3dFailed|Direct3D no disponible en la GPU|GPU Direct3D unavailable|Direct3D indisponível na GPU|Direct3D indisponible sur le GPU|Direct3D auf der GPU nicht verfügbar|Direct3D non disponibile sulla GPU
direct3dOk|Direct3D disponible en la GPU|GPU Direct3D available|Direct3D disponível na GPU|Direct3D disponible sur le GPU|Direct3D auf der GPU verfügbar|Direct3D disponibile sulla GPU
direct3dUnknown|Direct3D sin verificación individual|Direct3D not individually verified|Direct3D sem verificação individual|Direct3D non vérifié individuellement|Direct3D nicht einzeln geprüft|Direct3D non verificato individualmente
driverReview|Driver pendiente de revisión|Driver needs review|Driver precisa de revisão|Pilote à vérifier|Treiberprüfung erforderlich|Driver da verificare
driverOk|Driver activo firmado|Signed driver active|Driver assinado ativo|Pilote signé actif|Signierter Treiber aktiv|Driver firmato attivo
pnpError|Error de dispositivo PnP|PnP device error|Erro de dispositivo PnP|Erreur de périphérique PnP|PnP-Gerätefehler|Errore dispositivo PnP
pnpOk|Sin errores de dispositivo|No device errors|Sem erros de dispositivo|Aucune erreur de périphérique|Keine Gerätefehler|Nessun errore dispositivo
gpuPresent|Detectada por Windows|Detected by Windows|Detectada pelo Windows|Détecté par Windows|Von Windows erkannt|Rilevata da Windows
gpuAbsent|GPU no detectada|GPU not detected|GPU não detectada|GPU non détecté|GPU nicht erkannt|GPU non rilevata
home|Gráficos|Graphics|Gráficos|Graphiques|Grafik|Grafica
library|Biblioteca|Library|Biblioteca|Bibliothèque|Bibliothek|Libreria
recovery|Recuperación|Recovery|Recuperação|Récupération|Wiederherstellung|Ripristino
diagnostics|Diagnóstico|Diagnostics|Diagnóstico|Diagnostic|Diagnose|Diagnostica
settings|Ajustes|Settings|Configurações|Paramètres|Einstellungen|Impostazioni
portable|Portable · sin instalación|Portable · no setup|Portátil · sem instalação|Portable · sans installation|Portabel · ohne Setup|Portatile · senza installazione
headline|Controladores gráficos|Graphics drivers|Drivers gráficos|Pilotes graphiques|Grafiktreiber|Driver grafici
intro|Descarga, instalación y limpieza automáticas.|Automatic download, installation and cleanup.|Download, instalação e limpeza automáticos.|Téléchargement, installation et nettoyage automatiques.|Automatischer Download, Installation und Bereinigung.|Download, installazione e pulizia automatici.
detected|GRÁFICA DETECTADA|DETECTED GRAPHICS|PLACA DETECTADA|CARTE DÉTECTÉE|ERKANNTE GRAFIK|GRAFICA RILEVATA
scanning|Detectando tu equipo…|Detecting your computer…|Detectando seu computador…|Détection de votre PC…|Computer wird erkannt…|Rilevamento del PC…
driver|Driver activo|Active driver|Driver ativo|Pilote actif|Aktiver Treiber|Driver attivo
ready|Listo para empezar|Ready to start|Pronto para começar|Prêt à démarrer|Bereit zum Start|Pronto per iniziare
attention|Revisar driver|Check driver|Verificar driver|Vérifier le pilote|Treiber prüfen|Verifica driver
automatic|Instalación automática|Automatic installation|Instalação automática|Installation automatique|Automatische Installation|Installazione automatica
autoDetail|Descarga el driver compatible y limpia los paquetes antiguos sin uso.|Downloads a compatible driver and removes unused old packages.|Baixa o driver compatível e remove pacotes antigos sem uso.|Télécharge un pilote compatible et retire les anciens paquets inutilisés.|Lädt einen passenden Treiber und entfernt ungenutzte alte Pakete.|Scarica il driver compatibile e rimuove i vecchi pacchetti inutilizzati.
start|Instalar y limpiar|Install and clean up|Instalar e limpar|Installer et nettoyer|Installieren und bereinigen|Installa e pulisci
terms|Al iniciar, autorizas la instalación y aceptas la licencia del driver. Cierra juegos; la pantalla puede parpadear.|Starting authorizes installation and accepts the driver license. Close games; the screen may flicker.|Ao iniciar, você autoriza a instalação e aceita a licença do driver. Feche jogos; a tela pode piscar.|Démarrer autorise l’installation et accepte la licence du pilote. Fermez les jeux ; l’écran peut clignoter.|Der Start erlaubt die Installation und akzeptiert die Treiberlizenz. Spiele schließen; der Bildschirm kann flackern.|Avviando autorizzi l’installazione e accetti la licenza del driver. Chiudi i giochi; lo schermo può lampeggiare.
scope|Catálogo de Windows Update · driver de pantalla. Las suites del fabricante se instalan por separado.|Windows Update catalog · display driver. Vendor suites are installed separately.|Catálogo do Windows Update · driver de vídeo. As suítes do fabricante são instaladas separadamente.|Catalogue Windows Update · pilote graphique. Les suites du fabricant s’installent séparément.|Windows-Update-Katalog · Grafiktreiber. Hersteller-Suiten werden separat installiert.|Catalogo Windows Update · driver video. Le suite del produttore si installano separatamente.
rescan|Volver a detectar|Detect again|Detectar novamente|Détecter à nouveau|Erneut erkennen|Rileva di nuovo
system|Sistema|System|Sistema|Système|System|Sistema
disk|Espacio disponible|Available storage|Espaço disponível|Espace disponible|Freier Speicher|Spazio disponibile
protection|Protección|Protection|Proteção|Protection|Schutz|Protezione
backupFirst|Respaldo antes de instalar|Backup before installation|Backup antes de instalar|Sauvegarde avant installation|Sicherung vor Installation|Backup prima dell’installazione
workflow|El proceso|The workflow|O processo|Le processus|Der Ablauf|Il processo
search|Buscar compatible|Find compatible driver|Buscar driver compatível|Trouver un pilote compatible|Passenden Treiber suchen|Trova driver compatibile
download|Descargar y validar|Download and validate|Baixar e validar|Télécharger et valider|Laden und prüfen|Scarica e verifica
backup|Respaldar drivers|Back up drivers|Salvar drivers|Sauvegarder les pilotes|Treiber sichern|Backup dei driver
install|Instalar|Install|Instalar|Installer|Installieren|Installa
verify|Verificar y limpiar|Verify and clean up|Verificar e limpar|Vérifier et nettoyer|Prüfen und bereinigen|Verifica e pulisci
working|Trabajando…|Working…|Trabalhando…|En cours…|Wird ausgeführt…|Operazione in corso…
stop|Detener al finalizar el paso|Stop after this step|Parar após esta etapa|Arrêter après cette étape|Nach diesem Schritt stoppen|Ferma dopo questo passaggio
stopping|Se detendrá al finalizar el paso de Windows.|Will stop after the current Windows step finishes.|Parará após a etapa atual do Windows.|Arrêt à la fin de l’étape Windows en cours.|Stoppt nach dem aktuellen Windows-Schritt.|Si fermerà al termine del passaggio Windows.
cancelled|Operación detenida. Tus respaldos se conservan.|Stopped. Your backups are preserved.|Operação parada. Seus backups foram preservados.|Opération arrêtée. Vos sauvegardes sont conservées.|Gestoppt. Sicherungen bleiben erhalten.|Operazione fermata. I backup sono conservati.
details|Detalles técnicos|Technical details|Detalhes técnicos|Détails techniques|Technische Details|Dettagli tecnici
demo|Probar demo|Try demo|Testar demo|Essayer la démo|Demo testen|Prova demo
demoMode|DEMO · EQUIPO SIMULADO|DEMO · SIMULATED COMPUTER|DEMO · COMPUTADOR SIMULADO|DÉMO · PC SIMULÉ|DEMO · SIMULIERTER PC|DEMO · PC SIMULATO
live|Volver a mi equipo|Back to my computer|Voltar ao meu PC|Retour à mon PC|Zurück zu meinem PC|Torna al mio PC
demoDone|Demo completada. Ningún driver de tu PC fue modificado.|Demo complete. No drivers on your PC were changed.|Demo concluída. Nenhum driver do seu PC foi alterado.|Démo terminée. Aucun pilote de votre PC n’a été modifié.|Demo abgeschlossen. Keine PC-Treiber wurden verändert.|Demo completata. Nessun driver del PC è stato modificato.
done|Driver activo verificado. Preparación completada.|Active driver verified. Preparation complete.|Driver ativo verificado. Preparação concluída.|Pilote actif vérifié. Préparation terminée.|Aktiver Treiber geprüft. Vorbereitung abgeschlossen.|Driver attivo verificato. Preparazione completata.
noOffer|Sin nuevas ofertas en el catálogo. Driver activo verificado.|No new catalog offers. Active driver verified.|Sem novas ofertas no catálogo. Driver ativo verificado.|Aucune nouvelle offre. Pilote actif vérifié.|Keine neuen Katalogangebote. Aktiver Treiber geprüft.|Nessuna nuova offerta. Driver attivo verificato.
review|La instalación necesita verificación. Los drivers anteriores se conservan.|Installation needs verification. Previous drivers are preserved.|A instalação precisa ser verificada. Os drivers anteriores foram mantidos.|Installation à vérifier. Les anciens pilotes sont conservés.|Installation muss geprüft werden. Alte Treiber bleiben erhalten.|Installazione da verificare. I driver precedenti sono conservati.
reboot|Reinicia cuando estés listo. Swap continuará al iniciar sesión; Windows puede pedir UAC otra vez.|Restart when ready. Swap continues after sign-in; Windows may ask for UAC again.|Reinicie quando estiver pronto. Swap continua ao entrar; Windows pode pedir UAC novamente.|Redémarrez quand vous êtes prêt. Swap reprend à la connexion ; Windows peut redemander UAC.|Starte neu, wenn du bereit bist. Swap setzt nach der Anmeldung fort; Windows kann erneut UAC anfordern.|Riavvia quando sei pronto. Swap riprende al login; Windows può richiedere UAC di nuovo.
restart|Reiniciar Windows|Restart Windows|Reiniciar Windows|Redémarrer Windows|Windows neu starten|Riavvia Windows
restartConfirm|Guarda tu trabajo. ¿Reiniciar Windows ahora?|Save your work. Restart Windows now?|Salve seu trabalho. Reiniciar o Windows agora?|Enregistrez votre travail. Redémarrer Windows maintenant ?|Arbeit speichern. Windows jetzt neu starten?|Salva il lavoro. Riavviare Windows ora?
offlineTitle|Paquetes de drivers|Driver packages|Pacotes de drivers|Paquets de pilotes|Treiberpakete|Pacchetti driver
offlineIntro|Importa un paquete firmado para instalar sin conexión. La ruta automática no necesita archivos manuales.|Import a signed package to install offline. The automatic flow needs no manual files.|Importe um pacote assinado para instalar offline. O fluxo automático dispensa arquivos manuais.|Importez un pilote signé pour l’installer hors ligne. Le parcours automatique ne nécessite aucun fichier manuel.|Signiertes Paket für die Offline-Installation importieren. Der automatische Ablauf braucht keine manuellen Dateien.|Importa un pacchetto firmato per l’installazione offline. Il flusso automatico non richiede file manuali.
import|Importar paquete|Import package|Importar pacote|Importer un pilote|Paket importieren|Importa pacchetto
openFolder|Abrir carpeta|Open folder|Abrir pasta|Ouvrir le dossier|Ordner öffnen|Apri cartella
offlineInstall|Instalar paquete guardado|Install saved package|Instalar pacote salvo|Installer le pilote enregistré|Gespeichertes Paket installieren|Installa pacchetto salvato
emptyLibrary|La biblioteca está vacía.|Your library is empty.|A biblioteca está vazia.|La bibliothèque est vide.|Die Bibliothek ist leer.|La libreria è vuota.
selectPackage|Selecciona un paquete de la lista.|Select a package from the list.|Selecione um pacote da lista.|Sélectionnez un pilote dans la liste.|Paket aus der Liste wählen.|Seleziona un pacchetto dall’elenco.
packagePicker|Instalador oficial o INF de pantalla|Official installer or display INF|Instalador oficial ou INF de vídeo|Installateur officiel ou INF graphique|Offizieller Installer oder Grafik-INF|Installer ufficiale o INF video
offlineConfirm|¿Respaldar e instalar este paquete? El EXE del fabricante puede mostrar sus propias opciones.|Back up and install this package? A vendor EXE may show its own options.|Salvar e instalar este pacote? O EXE do fabricante pode mostrar opções próprias.|Sauvegarder et installer ce pilote ? L’EXE du fabricant peut afficher ses options.|Paket sichern und installieren? Ein Hersteller-EXE kann eigene Optionen anzeigen.|Eseguire backup e installare il pacchetto? L’EXE del produttore può mostrare le proprie opzioni.
imported|Paquete validado y guardado.|Package validated and saved.|Pacote validado e salvo.|Pilote validé et enregistré.|Paket geprüft und gespeichert.|Pacchetto verificato e salvato.
recoveryTitle|Respaldos y recuperación|Backups and recovery|Backups e recuperação|Sauvegardes et restauration|Sicherung und Wiederherstellung|Backup e ripristino
recoveryIntro|Los respaldos se crean automáticamente antes de cada cambio. La recuperación verifica firma y compatibilidad.|Backups are created automatically before each change. Recovery checks signature and compatibility.|Backups são criados automaticamente antes de cada mudança. A recuperação verifica assinatura e compatibilidade.|Les sauvegardes sont automatiques avant chaque changement. La récupération vérifie signature et compatibilité.|Vor jeder Änderung wird automatisch gesichert. Wiederherstellung prüft Signatur und Kompatibilität.|I backup sono automatici prima di ogni modifica. Il ripristino verifica firma e compatibilità.
restore|Restaurar este respaldo|Restore this backup|Restaurar este backup|Restaurer cette sauvegarde|Diese Sicherung wiederherstellen|Ripristina questo backup
restoreNote|Windows decide qué driver puede activarse. Si un respaldo antiguo no tiene prioridad, Swap lo informa y conserva el actual.|Windows decides which driver can become active. If an older backup has lower rank, Swap reports it and preserves the current driver.|O Windows decide qual driver pode ser ativado. Se o backup antigo tiver menor prioridade, o Swap informa e mantém o atual.|Windows choisit le pilote actif. Si l’ancien pilote est moins prioritaire, Swap le signale et conserve le pilote actuel.|Windows entscheidet, welcher Treiber aktiv wird. Bei niedrigerem Rang meldet Swap dies und behält den aktuellen Treiber.|Windows sceglie il driver attivo. Se un backup meno recente ha priorità inferiore, Swap lo segnala e conserva quello attuale.
emptyHistory|Todavía no hay sesiones.|No sessions yet.|Ainda não há sessões.|Aucune session pour le moment.|Noch keine Sitzungen.|Nessuna sessione.
selectBackup|Selecciona un respaldo de la lista.|Select a backup from the list.|Selecione um backup da lista.|Sélectionnez une sauvegarde.|Sicherung aus der Liste wählen.|Seleziona un backup dall’elenco.
restoreConfirm|¿Restaurar este driver? Se respaldará el actual y Windows comprobará el paquete.|Restore this driver? The current driver will be backed up and Windows will check the package.|Restaurar este driver? O atual será salvo e o Windows verificará o pacote.|Restaurer ce pilote ? Le pilote actuel sera sauvegardé et Windows vérifiera le paquet.|Treiber wiederherstellen? Aktueller Treiber wird gesichert und Windows prüft das Paket.|Ripristinare questo driver? Il driver attuale sarà salvato e Windows verificherà il pacchetto.
diagnosticsTitle|Estado del sistema|System status|Estado do sistema|État du système|Systemstatus|Stato del sistema
diagnosticsIntro|GPU, Direct3D, pantallas y cambios de driver.|GPU, Direct3D, displays and driver changes.|GPU, Direct3D, telas e alterações de driver.|GPU, Direct3D, écrans et changements de pilote.|GPU, Direct3D, Displays und Treiberänderungen.|GPU, Direct3D, schermi e modifiche ai driver.
export|Guardar informe|Save report|Salvar relatório|Enregistrer le rapport|Bericht speichern|Salva report
directx|Informe DirectX|DirectX report|Relatório DirectX|Rapport DirectX|DirectX-Bericht|Report DirectX
events|Eventos de pantalla|Display events|Eventos de vídeo|Événements graphiques|Grafikereignisse|Eventi video
saved|Informe guardado.|Report saved.|Relatório salvo.|Rapport enregistré.|Bericht gespeichert.|Report salvato.
signed|Firma verificada|Signature verified|Assinatura verificada|Signature vérifiée|Signatur geprüft|Firma verificata
unsigned|Firma sin confirmar|Signature unconfirmed|Assinatura não confirmada|Signature non confirmée|Signatur unbestätigt|Firma non confermata
healthy|Sin errores PnP|No PnP errors|Sem erros PnP|Aucune erreur PnP|Keine PnP-Fehler|Nessun errore PnP
settingsTitle|Configuración|Settings|Configurações|Paramètres|Einstellungen|Impostazioni
language|Idioma|Language|Idioma|Langue|Sprache|Lingua
systemLanguage|Idioma del sistema|System language|Idioma do sistema|Langue du système|Systemsprache|Lingua di sistema
appearance|Apariencia|Appearance|Aparência|Apparence|Darstellung|Aspetto
systemTheme|Tema del sistema|System theme|Tema do sistema|Thème du système|Systemdesign|Tema di sistema
dark|Oscuro|Dark|Escuro|Sombre|Dunkel|Scuro
light|Claro|Light|Claro|Clair|Hell|Chiaro
previous|Marca anterior|Previous vendor|Marca anterior|Ancien fabricant|Vorheriger Hersteller|Produttore precedente
autoDetect|Detectar automáticamente|Detect automatically|Detectar automaticamente|Détection automatique|Automatisch erkennen|Rileva automaticamente
previousHint|Solo se retiran paquetes de pantalla sin uso. Las GPU presentes conservan sus drivers.|Only unused display packages are removed. Present GPUs keep their drivers.|Somente pacotes de vídeo sem uso são removidos. GPUs presentes mantêm seus drivers.|Seuls les pilotes graphiques inutilisés sont retirés. Les cartes présentes conservent leurs pilotes.|Nur ungenutzte Grafikpakete werden entfernt. Vorhandene GPUs behalten ihre Treiber.|Vengono rimossi solo i pacchetti video inutilizzati. Le GPU presenti conservano i driver.
storage|Datos locales|Local data|Dados locais|Données locales|Lokale Daten|Dati locali
storageHint|Paquetes, respaldos e informes se guardan en tu perfil. Si existe una carpeta Data junto al EXE, se usa como modo USB.|Packages, backups and reports stay in your profile. A Data folder next to the EXE enables USB mode.|Pacotes, backups e relatórios ficam no seu perfil. Uma pasta Data ao lado do EXE ativa o modo USB.|Pilotes, sauvegardes et rapports restent dans votre profil. Un dossier Data à côté de l’EXE active le mode USB.|Pakete, Sicherungen und Berichte bleiben im Profil. Ein Data-Ordner neben der EXE aktiviert den USB-Modus.|Pacchetti, backup e report restano nel tuo profilo. Una cartella Data accanto all’EXE attiva la modalità USB.
about|Swap 0.4 · KraicDev · GPL-3.0-or-later|Swap 0.4 · KraicDev · GPL-3.0-or-later|Swap 0.4 · KraicDev · GPL-3.0-or-later|Swap 0.4 · KraicDev · GPL-3.0-or-later|Swap 0.4 · KraicDev · GPL-3.0-or-later|Swap 0.4 · KraicDev · GPL-3.0-or-later
support|Windows 10/11 · x64. La disponibilidad del driver depende de tu hardware y del catálogo. Las GPU aún no instaladas no se pueden detectar.|Windows 10/11 · x64. Driver availability depends on hardware and catalog. GPUs not yet installed cannot be detected.|Windows 10/11 · x64. Drivers dependem do hardware e do catálogo. GPUs ainda não instaladas não podem ser detectadas.|Windows 10/11 · x64. Les pilotes dépendent du matériel et du catalogue. Impossible de détecter une carte non installée.|Windows 10/11 · x64. Treiber hängen von Hardware und Katalog ab. Noch nicht eingebaute GPUs können nicht erkannt werden.|Windows 10/11 · x64. I driver dipendono da hardware e catalogo. Le GPU non ancora installate non sono rilevabili.
admin|Administrador|Administrator|Administrador|Administrateur|Administrator|Amministratore
readOnly|Solo lectura|Read only|Somente leitura|Lecture seule|Nur Lesen|Sola lettura
adminNeeded|Acepta el permiso de administrador al abrir Swap para instalar drivers.|Accept administrator permission when opening Swap to install drivers.|Aceite a permissão de administrador ao abrir o Swap para instalar drivers.|Acceptez les droits administrateur à l’ouverture de Swap pour installer les pilotes.|Beim Öffnen Administratorrechte akzeptieren, um Treiber zu installieren.|Accetta i permessi di amministratore all’apertura per installare i driver.
noGpu|No se detectó una GPU compatible.|No supported GPU detected.|Nenhuma GPU compatível detectada.|Aucune carte compatible détectée.|Keine unterstützte GPU erkannt.|Nessuna GPU compatibile rilevata.
manyGpu|Hay varias GPU de la misma marca. Usa un paquete local revisado para este equipo.|Multiple GPUs of the same vendor. This configuration needs individual review.|Há várias GPUs da mesma marca. Esta configuração precisa de revisão individual.|Plusieurs cartes du même fabricant. Cette configuration doit être vérifiée individuellement.|Mehrere GPUs desselben Herstellers. Diese Konfiguration erfordert eine Einzelprüfung.|Più GPU dello stesso produttore. La configurazione richiede una verifica individuale.
lowDisk|Se necesitan al menos 4 GB libres en Windows.|Windows needs at least 4 GB free.|O Windows precisa de pelo menos 4 GB livres.|Windows nécessite au moins 4 Go libres.|Windows benötigt mindestens 4 GB freien Speicher.|Windows richiede almeno 4 GB liberi.
remote|Abre Swap desde una sesión local para cambiar drivers.|Open Swap in a local session to change drivers.|Abra o Swap em uma sessão local para alterar drivers.|Ouvrez Swap dans une session locale pour modifier les pilotes.|Swap in einer lokalen Sitzung öffnen, um Treiber zu ändern.|Apri Swap in una sessione locale per modificare i driver.
failed|No se pudo completar. Consulta los detalles y vuelve a detectar.|Could not complete. Check details and detect again.|Não foi possível concluir. Veja os detalhes e detecte novamente.|Impossible de terminer. Consultez les détails et relancez la détection.|Nicht abgeschlossen. Details prüfen und erneut erkennen.|Operazione non completata. Controlla i dettagli e rileva di nuovo.
noCompatible|No hay un driver compatible disponible y el driver actual necesita atención. No se instaló ni eliminó nada.|No compatible driver available and the current driver needs attention. Nothing was installed or removed.|Nenhum driver compatível disponível e o atual precisa de atenção. Nada foi instalado ou removido.|Aucun pilote compatible disponible et le pilote actuel nécessite une vérification. Rien n’a été installé ni supprimé.|Kein passender Treiber verfügbar und der aktuelle Treiber muss geprüft werden. Nichts installiert oder entfernt.|Nessun driver compatibile disponibile e quello attuale richiede attenzione. Nulla installato o rimosso.
another|Otra instancia de Swap está trabajando.|Another Swap instance is working.|Outra instância do Swap está trabalhando.|Une autre instance de Swap est active.|Eine andere Swap-Instanz arbeitet bereits.|Un’altra istanza di Swap sta lavorando.
wait|Espera a que termine la operación actual.|Wait for the current operation to finish.|Aguarde a operação atual terminar.|Attendez la fin de l’opération en cours.|Auf das Ende des aktuellen Vorgangs warten.|Attendi il termine dell’operazione in corso.
oem|Equipo con gráficos híbridos: se conservan los drivers de todas las GPU presentes.|Hybrid graphics computer: drivers for all present GPUs are preserved.|Gráficos híbridos: drivers de todas as GPUs presentes são mantidos.|Graphiques hybrides : les pilotes de toutes les cartes présentes sont conservés.|Hybridgrafik: Treiber aller vorhandenen GPUs bleiben erhalten.|Grafica ibrida: i driver di tutte le GPU presenti sono conservati.
newDriver|Driver seleccionado|Selected driver|Driver selecionado|Pilote sélectionné|Ausgewählter Treiber|Driver selezionato
minimize|Minimizar|Minimize|Minimizar|Réduire|Minimieren|Riduci
maximize|Maximizar o restaurar|Maximize or restore|Maximizar ou restaurar|Agrandir ou restaurer|Maximieren oder wiederherstellen|Ingrandisci o ripristina
close|Cerrar|Close|Fechar|Fermer|Schließen|Chiudi
installed|Instalado|Installed|Instalado|Installé|Installiert|Installato
pending|Pendiente|Pending|Pendente|En attente|Ausstehend|In sospeso
stopped|Detenido|Stopped|Parado|Arrêté|Gestoppt|Fermato
complete|Completado|Complete|Concluído|Terminé|Abgeschlossen|Completato
confirm|Confirmar|Confirm|Confirmar|Confirmer|Bestätigen|Conferma
notDemo|Disponible en tu equipo real.|Available on your actual computer.|Disponível no seu computador real.|Disponible sur votre PC réel.|Auf deinem echten PC verfügbar.|Disponibile sul tuo PC reale.
selectGpu|GPU de destino|Target GPU|GPU de destino|Carte cible|Ziel-GPU|GPU di destinazione
checking|Verificando compatibilidad y permisos…|Checking compatibility and permissions…|Verificando compatibilidade e permissões…|Vérification de la compatibilité et des droits…|Kompatibilität und Rechte werden geprüft…|Verifica di compatibilità e permessi…
downloaded|Driver descargado y validado por Windows.|Driver downloaded and validated by Windows.|Driver baixado e validado pelo Windows.|Pilote téléchargé et validé par Windows.|Treiber von Windows geladen und geprüft.|Driver scaricato e verificato da Windows.
recoveryPending|Hay una sesión pendiente. Tras reiniciar, pulsa Preparar esta GPU para verificar y terminar.|There is a pending session. After restarting, choose Prepare this GPU to verify and finish.|Há uma sessão pendente. Após reiniciar, escolha Preparar esta GPU para verificar e concluir.|Une session est en attente. Après redémarrage, préparez la carte pour vérifier et terminer.|Eine Sitzung ist offen. Nach dem Neustart diese GPU vorbereiten, um zu prüfen und abzuschließen.|Sessione in sospeso. Dopo il riavvio, prepara la GPU per verificare e completare.
runImported|Se instalará el paquete seleccionado. El respaldo se hace primero.|The selected package will be installed. Backup runs first.|O pacote selecionado será instalado. O backup vem primeiro.|Le pilote sélectionné sera installé après sauvegarde.|Das gewählte Paket wird nach der Sicherung installiert.|Il pacchetto selezionato sarà installato dopo il backup.
unsupported|Esta versión requiere Windows 10/11 x64.|This version requires Windows 10/11 x64.|Esta versão exige Windows 10/11 x64.|Cette version nécessite Windows 10/11 x64.|Diese Version benötigt Windows 10/11 x64.|Questa versione richiede Windows 10/11 x64.
networkHint|La búsqueda usa tu servicio de Windows Update. Necesita conexión y puede tardar varios minutos.|Search uses your Windows Update service. It needs a connection and may take several minutes.|A busca usa seu serviço Windows Update. Requer conexão e pode levar vários minutos.|La recherche utilise votre service Windows Update. Connexion requise ; cela peut prendre plusieurs minutes.|Die Suche nutzt deinen Windows-Update-Dienst. Verbindung nötig; es kann mehrere Minuten dauern.|La ricerca usa il tuo servizio Windows Update. Richiede connessione e può durare alcuni minuti.
notLatest|El catálogo puede ofrecer una versión anterior a la del fabricante.|The catalog may offer an older version than the vendor website.|O catálogo pode oferecer uma versão anterior à do fabricante.|Le catalogue peut proposer une version plus ancienne que le fabricant.|Der Katalog kann eine ältere Version als die Herstellerseite anbieten.|Il catalogo può offrire una versione meno recente di quella del produttore.
files|Archivos guardados|Saved files|Arquivos salvos|Fichiers enregistrés|Gespeicherte Dateien|File salvati
history|Sesiones recientes|Recent sessions|Sessões recentes|Sessions récentes|Letzte Sitzungen|Sessioni recenti
backups|Respaldos disponibles|Available backups|Backups disponíveis|Sauvegardes disponibles|Verfügbare Sicherungen|Backup disponibili
downloadOnly|Solo descargar|Download only|Somente baixar|Télécharger uniquement|Nur herunterladen|Solo scarica
alreadyReady|Sin descarga pendiente para esta GPU.|No pending download for this GPU.|Sem download pendente para esta GPU.|Aucun téléchargement en attente pour cette carte.|Kein ausstehender Download für diese GPU.|Nessun download in sospeso per questa GPU.
noAutoReboot|Swap no reinicia sin tu confirmación.|Swap never restarts without your confirmation.|O Swap não reinicia sem sua confirmação.|Swap ne redémarre jamais sans votre accord.|Swap startet niemals ohne Bestätigung neu.|Swap non riavvia senza la tua conferma.
license|Licencia GPL-3.0-or-later y documentación|GPL-3.0-or-later license and documentation|Licença GPL-3.0-or-later e documentação|Licence GPL-3.0-or-later et documentation|GPL-3.0-or-later-Lizenz und Dokumentation|Licenza GPL-3.0-or-later e documentazione
sourceLink|Fuentes oficiales|Official sources|Fontes oficiais|Sources officielles|Offizielle Quellen|Fonti ufficiali
suiteLink|Driver y software del fabricante|Vendor driver and software|Driver e software do fabricante|Pilote et logiciel du fabricant|Treiber und Software des Herstellers|Driver e software del produttore
healthyScope|Verifica firma y estado PnP; comprueba tus juegos después.|Checks signature and PnP status; test your games afterwards.|Verifica assinatura e estado PnP; teste seus jogos depois.|Vérifie signature et état PnP ; testez ensuite vos jeux.|Prüft Signatur und PnP-Status; Spiele danach testen.|Verifica firma e stato PnP; prova i giochi dopo.
demoBackup|Respaldo simulado · no restaurable|Simulated backup · cannot restore|Backup simulado · não restaurável|Sauvegarde simulée · non restaurable|Simulierte Sicherung · nicht wiederherstellbar|Backup simulato · non ripristinabile
adminRetry|Solicitar permisos|Request permissions|Solicitar permissões|Demander les droits|Berechtigungen anfordern|Richiedi permessi
recoveryUnsupported|Este respaldo no corresponde a la GPU instalada.|This backup does not match the installed GPU.|Este backup não corresponde à GPU instalada.|Cette sauvegarde ne correspond pas à la carte installée.|Diese Sicherung passt nicht zur eingebauten GPU.|Questo backup non corrisponde alla GPU installata.
windowMode|Ventana|Window|Janela|Fenêtre|Fenster|Finestra
""";
    private static readonly Dictionary<string, string[]> Rows = Catalog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r').Split('|')).ToDictionary(row => row[0], row => row[1..]);
    public static IReadOnlyDictionary<string, string[]> Translations => Rows;
    public static string T(string key) => Rows.TryGetValue(key, out var row) ? row[Array.IndexOf(Languages, Language)].Replace("x64", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()) : key;
    public static void Apply(string choice)
    {
        var language = choice == "auto" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : choice;
        Language = Languages.Contains(language) ? language : "en";
        foreach (var key in Rows.Keys) Application.Current.Resources[key] = T(key);
    }
}

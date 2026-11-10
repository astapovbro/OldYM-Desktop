from pathlib import Path
import hashlib, zipfile, shutil, json
ROOT=Path(__file__).resolve().parent
OUT=ROOT.parent/'OldYM-Desktop-release'
OUT.mkdir(exist_ok=True)
version='0.2.1-preview'
status='''# Состояние проверки OldYM Desktop Legacy 0.2.1 preview\n\nИзменения: объединение собственных и сохранённых плейлистов с удалением дублей; обложка из альбома или дополнительных данных трека; загрузка 200×200 и уменьшение до 128×128; ошибки обложек отображаются; необязательный логотип assets/sidebar.png рядом с названием.\nПроверки именно 0.2.1 смотри в RELEASE-CHECKS.txt. Авторизация и работа с реальными плейлистами/обложками в этом обновлении не проверены.\n\nИстория предыдущей версии:\n\n- В версии 0.2 повторно пройдено 40 проверок ядра.\n- Новое оформление: чистый список, боковая навигация, единый нижний плеер, лёгкие нарисованные кнопки и шкала перемотки. Без браузера и новых зависимостей.\n- Для версии 0.1 пользователь подтвердил поиск и воспроизведение; новая 0.2 на Windows пока не проверялась.\n- Ранее пройдено 11 локальных HTTPS-проверок: TLS 1.2, доверенный сертификат, chunked, относительный HTTPS-redirect, обрезанный ответ, запрет HTTPS→HTTP, ограничение redirect, неизвестный CA, чужое имя, истёкший сертификат, неподходящий EKU, отсутствие токена на постороннем хосте.\n- С настоящим API установлено HTTPS-соединение и получен ожидаемый HTTP 401 без авторизации.\n- Главное окно, диалог входа и минимальное окно открыты под Mono/Xvfb; снимки просмотрены ранее.\n- Сборка имеет формат PE32, Intel i386, managed .NET; x86.\n\nНе проверены: настоящие Windows XP–8.1, сборка через Windows build.cmd/MSBuild, авторизация аккаунтом, выдача полного аудио, воспроизведение Windows winmm/MCI, сохранение входа через Windows DPAPI, живое продолжение волны.\n\nДемонстрационный режим предназначен только для просмотра интерфейса, не имитирует работающий онлайн-плеер.\n'''
(ROOT/'TEST-STATUS.md').write_text(status,encoding='utf-8')
base=['README.md','TEST-STATUS.md','LICENSE','RELEASE-CHECKS.txt']
base += [str(p.relative_to(ROOT)) for p in (ROOT/'licenses').iterdir() if p.is_file()]
app={p:ROOT/p for p in base}
for name in ['OldYM.Desktop.exe','OldYM.Desktop.exe.config','BouncyCastle.Crypto.dll']:
 app[name]=ROOT/'bin'/name
for p in (ROOT/'bin/assets').iterdir():
 if p.is_file(): app['assets/'+p.name]=p
source={p:ROOT/p for p in base}
for folder in ['src','assets','tests']:
 for p in (ROOT/folder).rglob('*'):
  if p.is_file() and '__pycache__' not in p.parts and p.suffix not in ('.pyc','.exe','.dll','.key'):
   source[str(p.relative_to(ROOT))]=p
for name in ['build.cmd','build.sh','OldYM.Desktop.csproj','OldYM.Desktop.exe.config','package_release.py']:
 source[name]=ROOT/name
source['lib/BouncyCastle.Crypto.dll']=ROOT/'lib/BouncyCastle.Crypto.dll'
for mapping in (app,source):
 for name,path in mapping.items():
  assert path.is_file(),str(path)
  assert not name.endswith(('.key','.pfx','.p12','.keystore'))
  assert 'account.dat' not in name
paths=[]
for label,mapping in [('windows-x86',app),('source',source)]:
 dest=OUT/('OldYM-Desktop-'+version+'-'+label+'.zip')
 with zipfile.ZipFile(dest,'w',zipfile.ZIP_DEFLATED) as z:
  for name,path in sorted(mapping.items()):
   z.write(path,'OldYM-Desktop-'+version+'/'+name)
 with zipfile.ZipFile(dest) as z:
  assert z.testzip() is None
  for name,path in mapping.items(): assert z.read('OldYM-Desktop-'+version+'/'+name)==path.read_bytes(),name
 paths.append(dest)
 print('PASS archive bytes and CRC',dest.name,dest.stat().st_size,'bytes',len(mapping),'files')
check=OUT/'archive-check'
check.mkdir(exist_ok=True)
with zipfile.ZipFile(paths[0]) as z:z.extractall(check)
manifest=''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in paths)
(OUT/'SHA256SUMS.txt').write_text(manifest,encoding='ascii')
print(manifest)
print('PASS runtime archive: EXE, DLL, config, roots, icon, README, licenses')

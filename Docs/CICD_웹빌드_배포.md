# CI/CD - 태그 푸시로 WebGL 웹빌드 배포

`v`로 시작하는 태그를 푸시하면 GitHub Actions가 WebGL 빌드를 만들어 GitHub Pages에 올린다.

```
git tag v0.1.0
git push origin v0.1.0
```

배포 주소: `https://ashuatz.github.io/nhn-rush/`

## 구성 파일

| 파일 | 역할 |
| --- | --- |
| `.github/workflows/webgl-pages.yml` | 태그 푸시 → WebGL 빌드 → Pages 배포 |
| `.github/workflows/unity-activation.yml` | 라이선스 `.ulf` 발급용 활성화 파일 요청 (최초 1회 수동) |
| `Assets/Scripts/Editor/WebGLBuildPipeline.cs` | 배치모드 빌드 진입점 (Pages용 설정 적용) |

## 최초 1회 세팅

### 1. GitHub Pages를 Actions 배포로 전환

레포 `Settings` → `Pages` → `Build and deployment` → `Source`를 **GitHub Actions**로 바꾼다.
(`Deploy from a branch`로 두면 워크플로가 배포 단계에서 실패한다.)

### 2. github-pages 환경에서 태그 배포 허용

`Settings` → `Environments` → `github-pages` → `Deployment branches and tags`를 확인한다.
기본값이 "기본 브랜치만 허용"이면 **`v*` 태그로 돌린 실행은 배포 단계에서 차단된다.**
`Selected branches and tags`로 바꾸고 아래 두 규칙을 추가한다.

- Ref type `Branch` → `main`
- Ref type `Tag` → `v*`

(이 환경은 Pages를 Actions 배포로 전환하면 자동으로 생긴다. 항목이 안 보이면 1번을 먼저 한다.)

### 3. Unity 라이선스 시크릿 등록

GameCI의 `UNITY_LICENSE` 시크릿은 **ULF 형식(`.ulf`)** 라이선스 파일만 받는다.

주의: Unity 6 + Hub 3.x 환경은 로컬에 `.ulf`를 만들지 않는다.
`%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml` 이 대신 생기는데,
**이 파일은 형식이 달라서 `UNITY_LICENSE`에 넣어도 동작하지 않는다.**
(2026-08 기준 이 프로젝트 개발 PC가 이 상태다. `.ulf`가 시스템에 없다.)

그래서 아래 활성화 절차로 `.ulf`를 따로 발급받는다.

1. `Actions` 탭 → `Unity Activation File` → `Run workflow` 실행.
2. 실행이 끝나면 아티팩트로 `.alf` 파일이 나온다. 내려받아 압축을 푼다.
3. https://license.unity3d.com/manual 에 접속해 `.alf`를 업로드하고,
   Unity 계정으로 로그인해 `Unity Personal Edition`을 선택한다.
4. 받은 `.ulf` 파일을 텍스트 에디터로 열어 **내용 전체(XML)** 를 복사한다.
5. 레포 `Settings` → `Secrets and variables` → `Actions`에 아래 3개를 등록한다.

| 시크릿 | 값 |
| --- | --- |
| `UNITY_LICENSE` | `.ulf` 파일 내용 전체 (XML) |
| `UNITY_EMAIL` | Unity 계정 이메일 |
| `UNITY_PASSWORD` | Unity 계정 비밀번호 |

발급받은 `.ulf`는 레포에 커밋하지 말고 안전한 곳에 따로 보관한다.

이미 `.ulf`를 가진 환경(구버전 Unity에서 활성화한 PC 등)이라면 1~3번을 건너뛰고
`C:\ProgramData\Unity\Unity_lic.ulf` 내용을 그대로 4번부터 진행하면 된다.

Pro/Plus 라이선스라면 `UNITY_LICENSE` 대신 `UNITY_SERIAL`을 등록하고
`webgl-pages.yml`의 `env` 항목을 그에 맞게 바꾼다. 이 경우 활성화 절차가 필요 없다.

### 4. 확인

`Actions` 탭에서 `WebGL Pages` 워크플로를 `Run workflow`로 한 번 수동 실행해 본다.
정상이면 태그 없이도 현재 브랜치 기준으로 빌드/배포가 돈다.

## 동작 방식

1. **Free disk space** - Unity 에디터 도커 이미지가 7GB를 넘어서 러너 기본 여유 공간으로는 모자란다. 안 쓰는 SDK를 지운다.
2. **Cache Library** - `Library/` 폴더를 캐시한다. 첫 빌드는 30~60분, 이후는 훨씬 짧다.
3. **Build** - `game-ci/unity-builder`가 `unityci/editor:6000.3.20f1-webgl` 이미지로 배치모드 빌드를 돌린다.
   진입점은 `Rush.EditorTools.WebGLBuildPipeline.BuildForPages`.
4. **Prepare artifact** - 컨테이너가 root로 만든 산출물의 소유권을 되돌리고 `.nojekyll`을 넣는다.
5. **Deploy** - `actions/deploy-pages`로 Pages에 올린다.

빌드 대상 씬은 `EditorBuildSettings`(Build Profiles의 씬 목록)를 그대로 따른다.
새 씬을 배포에 넣으려면 빌드 설정에 추가하고 커밋해야 한다.

버전은 태그 이름이 그대로 `PlayerSettings.bundleVersion`에 들어간다.

## Pages 전용 설정 (중요)

GitHub Pages는 정적 호스팅이라 `Content-Encoding: gzip` 헤더를 붙여 주지 못한다.
프로젝트 설정은 Gzip 압축 + 폴백 OFF 상태인데, 이대로 올리면 로더가 압축 파일을 해석하지 못해
**검은 화면에서 멈춘다.**

그래서 `WebGLBuildPipeline.ApplyPagesFriendlySettings()`에서 빌드 직전에 강제로 바꾼다.

```csharp
PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
PlayerSettings.WebGL.decompressionFallback = true;  // 필수
```

CI 컨테이너 안에서만 바뀌므로 로컬 프로젝트 설정은 그대로다.

## 문제가 생기면

**빌드 단계에서 라이선스 오류**
`UnityEntitlementLicense.xml` 내용을 넣지 않았는지 먼저 확인한다. 그건 형식이 달라서 안 된다.
`.ulf`가 맞다면 내용이 잘렸거나 개행이 깨졌을 확률이 높다. 시크릿을 다시 등록한다.
`UNITY_EMAIL` / `UNITY_PASSWORD`가 비어 있어도 실패한다.

**배포 단계에서 "Branch/tag not allowed to deploy"**
`github-pages` 환경의 배포 ref 규칙에 `v*` 태그가 빠져 있다. 최초 세팅 2번을 확인한다.

**빌드가 진행 없이 멈춘다**
`Packages/MCPForUnity`가 에디터 로드 시 브리지 서버를 띄우려다 배치모드에서 걸릴 수 있다.
로그에 MCPForUnity 관련 메시지 이후로 진행이 없으면, 배포 전용으로 해당 패키지를 `manifest.json`에서 빼고 태그를 다시 만든다.

**Unity 버전을 올린 경우**
`webgl-pages.yml`과 `unity-activation.yml`의 `unityVersion` 두 곳을 `ProjectSettings/ProjectVersion.txt`와 맞춘다.
해당 버전 이미지가 있는지는 https://game.ci/docs/docker/versions 에서 확인한다.

**디스크 부족(No space left on device)**
`Free disk space` 단계에 지울 경로를 더 추가한다. (`/opt/hostedtoolcache` 등)

**같은 태그로 다시 배포하고 싶을 때**
```
git tag -d v0.1.0
git push origin :refs/tags/v0.1.0
git tag v0.1.0
git push origin v0.1.0
```

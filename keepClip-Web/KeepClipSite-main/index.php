<?php
// index.php
session_start();

if (isset($_GET['lang']) && in_array($_GET['lang'], ['ru', 'en'])) {
    $_SESSION['lang'] = $_GET['lang'];
}

$lang = $_SESSION['lang'] ?? 'ru';
$texts = include 'lang.php';
$t = $texts[$lang];
?>
<!DOCTYPE html>
<html lang="<?php echo $lang; ?>">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title><?php echo $t['title']; ?></title>
    <link rel="stylesheet" href="style.css">
</head>
<body>

    <!-- Анимированные источники света -->
    <div class="light-blob blob-purple"></div>
    <div class="light-blob blob-blue"></div>

    <div class="glass-viewport">

        <!-- ВЕРХНЯЯ ШАПКА -->
        <header class="main-header">
            <div class="brand-logo">
                    <img src="icons/logo.png" width="26" height="26">
                <span>KEEP<b>CLIP</b></span>
            </div>
            
            <nav class="center-nav">
                <a href="#"><?php echo $t['nav_features']; ?></a>
                <a href="#"><?php echo $t['nav_software']; ?></a>
                <a href="#"><?php echo $t['nav_discover']; ?></a>
                <a href="https://discord.gg/nZ3BK7BPKv"><?php echo $t['nav_support']; ?></a>
            </nav>

            <div class="right-actions">
                <a href="#" class="btn-login"><?php echo $t['nav_login']; ?></a>
                
                <form action="index.php" method="GET" id="langForm">
                    <select name="lang" id="langSelect">
                        <option value="ru" <?php echo $lang === 'ru' ? 'selected' : ''; ?>>RU</option>
                        <option value="en" <?php echo $lang === 'en' ? 'selected' : ''; ?>>EN</option>
                    </select>
                </form>
            </div>
        </header>

        <!-- ГЛАВНЫЙ ЭКРАН -->
        <section class="hero-section">
            <div class="hero-info">
                <h1 class="hero-title"><?php echo $t['hero_title']; ?></h1>
                <p class="hero-description"><?php echo $t['hero_desc']; ?></p>
                <button class="btn-cta-download" id="downloadBtn">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="black" style="vertical-align: middle; margin-right: 8px;"><path d="M0 3.449L9.75 2.1v9.451H0m10.949-9.602L24 0v11.4H10.949M0 12.6h9.75v9.451L0 20.699M10.949 12.6H24V24l-12.951-1.801"/></svg>
                    <?php echo $t['btn_download']; ?>
                </button>
            </div>
            
            <div class="hero-visual">
                <!-- Окно программы в стиле Windows -->
                <div class="software-preview-window">
                    <div class="window-header-win">
                        <div class="window-title-win">KeepClip</div>
                        <div class="win-controls">
                            <span class="win-btn minimize">&#8211;</span>
                            <span class="win-btn maximize">&#9723;</span>
                            <span class="win-btn close">&#10005;</span>
                        </div>
                    </div>
                    <div class="window-body-mock" style="padding: 0; overflow: hidden; position: relative;">
                        <!-- Видео для главного экрана. Закинь hero-clip.mp4 в папку videos -->
                        <video autoplay loop muted playsinline class="hero-video">
                            <source src="videos/hero-clip.mp4" type="video/mp4">
                        </video>
                    </div>
                </div>
            </div>
        </section>

        <!-- БЛОК ПРЕИМУЩЕСТВ (вместо статистики) -->
        <section class="features-grid">
            <div class="feature-card">
                <div class="feature-icon">
                    <svg class="front-svg" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M15.2683 18.2287C13.2889 20.9067 12.2992 22.2458 11.3758 21.9628C10.4525 21.6798 10.4525 20.0375 10.4525 16.7528L10.4526 16.4433C10.4526 15.2585 10.4526 14.6662 10.074 14.2946L10.054 14.2754C9.6673 13.9117 9.05079 13.9117 7.81775 13.9117C5.59888 13.9117 4.48945 13.9117 4.1145 13.2387C4.10829 13.2276 4.10225 13.2164 4.09639 13.205C3.74244 12.5217 4.3848 11.6526 5.66953 9.91436L8.73167 5.77133C10.711 3.09327 11.7007 1.75425 12.6241 2.03721C13.5474 2.32018 13.5474 3.96249 13.5474 7.24712V7.55682C13.5474 8.74151 13.5474 9.33386 13.926 9.70541L13.946 9.72466C14.3327 10.0884 14.9492 10.0884 16.1822 10.0884C18.4011 10.0884 19.5106 10.0884 19.8855 10.7613C19.8917 10.7724 19.8977 10.7837 19.9036 10.795C20.2576 11.4784 19.6152 12.3475 18.3304 14.0857" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>
                </div>
                <h3 class="feature-title"><?php echo $t['feat1_title']; ?></h3>
                <p class="feature-desc"><?php echo $t['feat1_desc']; ?></p>
            </div>
            <div class="feature-card">
                <div class="feature-icon">
                    <svg class="front-svg" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M17 2.5L17 21.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M7 2.5L7 21.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M2 12L7 12M22 12L17 12" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M2.5 7L7 7M21.5 7L17 7" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M21.5 17.75C21.9142 17.75 22.25 17.4142 22.25 17C22.25 16.5858 21.9142 16.25 21.5 16.25V17.75ZM17 16.25C16.5858 16.25 16.25 16.5858 16.25 17C16.25 17.4142 16.5858 17.75 17 17.75V16.25ZM7 17.75C7.41421 17.75 7.75 17.4142 7.75 17C7.75 16.5858 7.41421 16.25 7 16.25L7 17.75ZM17 17.75L21.5 17.75V16.25L17 16.25V17.75ZM2 17.75L7 17.75L7 16.25L2 16.25L2 17.75Z" fill="#ffffff"></path> <path d="M14 12C14 11.4722 13.4704 11.1162 12.4112 10.4043C11.3375 9.68271 10.8006 9.3219 10.4003 9.58682C10 9.85174 10 10.5678 10 12C10 13.4322 10 14.1483 10.4003 14.4132C10.8006 14.6781 11.3375 14.3173 12.4112 13.5957C13.4704 12.8838 14 12.5278 14 12Z" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M2 12C2 7.28595 2 4.92893 3.46447 3.46447C4.92893 2 7.28595 2 12 2C16.714 2 19.0711 2 20.5355 3.46447C21.352 4.28094 21.7133 5.37486 21.8731 7M22 12C22 16.714 22 19.0711 20.5355 20.5355C19.0711 22 16.714 22 12 22C7.28595 22 4.92893 22 3.46447 20.5355C2.64799 19.7191 2.28672 18.6251 2.12687 17" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>
                </div>
                <h3 class="feature-title"><?php echo $t['feat2_title']; ?></h3>
                <p class="feature-desc"><?php echo $t['feat2_desc']; ?></p>
            </div>
            <div class="feature-card">
                <div class="feature-icon">
                    <svg class="front-svg" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M14.1625 18.4876L13.4417 19.2084C11.053 21.5971 7.18019 21.5971 4.79151 19.2084C2.40283 16.8198 2.40283 12.9469 4.79151 10.5583L5.51236 9.8374" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M9.8374 14.1625L14.1625 9.8374" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> <path d="M9.8374 5.51236L10.5583 4.79151C12.9469 2.40283 16.8198 2.40283 19.2084 4.79151M18.4876 14.1625L19.2084 13.4417C20.4324 12.2177 21.0292 10.604 20.9988 9" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>
                </div>
                <h3 class="feature-title"><?php echo $t['feat3_title']; ?></h3>
                <p class="feature-desc"><?php echo $t['feat3_desc']; ?></p>
            </div>
        </section>

        <!-- ВИТРИНА КЛИПОВ -->
        <section class="showcase-section">
            <div style="text-align:center; margin-bottom:40px;">
                <div class="section-badge"><?php echo $t['showcase_badge']; ?></div>
                <h2 class="section-heading" style="margin-bottom:0;"><?php echo $t['showcase_heading']; ?></h2>
            </div>
            
            <div class="showcase-grid">
                <!-- Клип 1 -->
                <div class="clip-card">
                    <video autoplay loop muted playsinline class="clip-video">
                        <source src="videos/clip1.mp4" type="video/mp4">
                    </video>
                    <div class="clip-info">
                        <span class="clip-game">CS2</span>
                    </div>
                </div>
                
                <!-- Клип 2 -->
                <div class="clip-card">
                    <video autoplay loop muted playsinline class="clip-video">
                        <source src="videos/clip2.mp4" type="video/mp4">
                    </video>
                    <div class="clip-info">
                        <span class="clip-game">Valorant</span>
                    </div>
                </div>

                <!-- Клип 3 -->
                <div class="clip-card">
                    <video autoplay loop muted playsinline class="clip-video">
                        <source src="videos/clip3.mp4" type="video/mp4">
                    </video>
                    <div class="clip-info">
                        <span class="clip-game">World of Tanks (Mir Tankov)</span>
                    </div>
                </div>
            </div>
        </section>

        <!-- РАЗДЕЛ "О ПРОГРАММЕ" -->
        <section class="about-app-section">
            <h2 class="section-heading"><?php echo $t['about_title']; ?></h2>
            
            <div class="steps-container">
                <div class="step-card">
                    <div class="step-number"><?php echo $t['step1_num']; ?></div>
                    <h3 class="step-title"><?php echo $t['step1_title']; ?></h3>
                    <p class="step-desc"><?php echo $t['step1_desc']; ?></p>
                </div>
                <div class="step-arrow">➔</div>
                <div class="step-card">
                    <div class="step-number"><?php echo $t['step2_num']; ?></div>
                    <h3 class="step-title"><?php echo $t['step2_title']; ?></h3>
                    <p class="step-desc"><?php echo $t['step2_desc']; ?></p>
                </div>
                <div class="step-arrow">➔</div>
                <div class="step-card">
                    <div class="step-number text-glow-cyan"><?php echo $t['step3_num']; ?></div>
                    <h3 class="step-title"><?php echo $t['step3_title']; ?></h3>
                    <p class="step-desc"><?php echo $t['step3_desc']; ?></p>
                </div>
            </div>
        </section>
<section class="reviews-section">
            <div style="text-align:center; margin-bottom:40px;">
                <div class="section-badge"><?php echo $t['reviews_badge']; ?></div>
                <h2 class="section-heading" style="margin-bottom:0;"><?php echo $t['reviews_heading']; ?></h2>
            </div>
            <div class="reviews-grid">
                <div class="review-card">
                    <div class="review-stars">★★★★★</div>
                    <p class="review-text"><?php echo $t['rev1_text']; ?></p>
                    <div class="review-author">
                        <div class="avatar av-purple">AK</div>
                        <div>
                            <div class="author-name">Alexei_K</div>
                            <div class="author-sub"><?php echo $t['rev1_sub']; ?></div>
                        </div>
                    </div>
                </div>
                <div class="review-card">
                    <div class="review-stars">★★★★★</div>
                    <p class="review-text"><?php echo $t['rev2_text']; ?></p>
                    <div class="review-author">
                        <div class="avatar av-blue">NV</div>
                        <div>
                            <div class="author-name">Nova_V</div>
                            <div class="author-sub"><?php echo $t['rev2_sub']; ?></div>
                        </div>
                    </div>
                </div>
                <div class="review-card">
                    <div class="review-stars">★★★★☆</div>
                    <p class="review-text"><?php echo $t['rev3_text']; ?></p>
                    <div class="review-author">
                        <div class="avatar av-green">MZ</div>
                        <div>
                            <div class="author-name">mrZ</div>
                            <div class="author-sub"><?php echo $t['rev3_sub']; ?></div>
                        </div>
                    </div>
                </div>
            </div>
        </section>

        <section class="screenshots-section">
            <div style="text-align:center; margin-bottom:40px;">
                <div class="section-badge"><?php echo $t['specs_badge']; ?></div>
                <h2 class="section-heading" style="margin-bottom:0;"><?php echo $t['specs_heading']; ?></h2>
            </div>
            <div class="screenshots-row">
                <div class="shot-big">
                    <div class="window-header-win">
                        <div class="window-title-win">KeepClip — Performance Monitor</div>
                        <div class="win-controls">
                            <span class="win-btn minimize">&#8211;</span>
                            <span class="win-btn maximize">&#9723;</span>
                            <span class="win-btn close">&#10005;</span>
                        </div>
                    </div>
                    <div class="shot-body">
                        <div class="shot-tag"><?php echo $t['specs_tag1']; ?></div>
                        <div class="shot-label"><?php echo $t['specs_label1']; ?></div>
                        <div style="width:100%;display:flex;flex-direction:column;gap:10px;margin-top:8px;">
                            <div class="bar-row">
                                <span class="bar-label" style="font-size:12px;color:#666;">GPU Encode</span>
                                <div class="bar-track"><div class="bar-fill-p" style="width:18%;"></div></div>
                                <span class="bar-val">18%</span>
                            </div>
                            <div class="bar-row">
                                <span class="bar-label" style="font-size:12px;color:#666;">CPU</span>
                                <div class="bar-track"><div class="bar-fill-p" style="width:4%;"></div></div>
                                <span class="bar-val">4%</span>
                            </div>
                            <div class="bar-row">
                                <span class="bar-label" style="font-size:12px;color:#666;">RAM</span>
                                <div class="bar-track"><div class="bar-fill-b" style="width:35%;"></div></div>
                                <span class="bar-val">180 MB</span>
                            </div>
                            <div class="bar-row">
                                <span class="bar-label" style="font-size:12px;color:#666;">FPS Drop</span>
                                <div class="bar-track"><div class="bar-fill-p" style="width:2%;background:#27c93f;"></div></div>
                                <span class="bar-val" style="color:#27c93f;">~0%</span>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="shot-sm-col">
                    <div class="shot-sm">
                        <div class="window-header-win">
                            <div class="window-title-win"></div>
                            <div class="win-controls">
                                <span class="win-btn minimize">&#8211;</span>
                                <span class="win-btn maximize">&#9723;</span>
                                <span class="win-btn close">&#10005;</span>
                            </div>
                        </div>
                        <div class="shot-body">
                            <div class="shot-tag"><?php echo $t['specs_tag2']; ?></div>
                            <div class="shot-label">H.264 / H.265 / AV1</div>
                            <div class="shot-sub"><?php echo $t['specs_sub2']; ?></div>
                        </div>
                    </div>
                    <div class="shot-sm">
                        <div class="window-header-win">
                            <div class="window-title-win"></div>
                            <div class="win-controls">
                                <span class="win-btn minimize">&#8211;</span>
                                <span class="win-btn maximize">&#9723;</span>
                                <span class="win-btn close">&#10005;</span>
                            </div>
                        </div>
                        <div class="shot-body">
                            <div class="shot-tag"><?php echo $t['specs_tag3']; ?></div>
                            <div class="shot-label"><?php echo $t['specs_label3']; ?></div>
                            <div class="shot-sub"><?php echo $t['specs_sub3']; ?></div>
                        </div>
                    </div>
                </div>
            </div>
        </section>

        <section class="faq-section">
            <div style="text-align:center; margin-bottom:40px;">
                <div class="section-badge"><?php echo $t['faq_badge']; ?></div>
                <h2 class="section-heading" style="margin-bottom:0;"><?php echo $t['faq_heading']; ?></h2>
            </div>
            <div class="faq-list" id="faqList">
                <div class="faq-item">
                    <button class="faq-q">
                        <?php echo $t['faq_q1']; ?>
                        <span class="faq-arrow">▾</span>
                    </button>
                    <div class="faq-a"><?php echo $t['faq_a1']; ?></div>
                </div>
                <div class="faq-item">
                    <button class="faq-q">
                        <?php echo $t['faq_q2']; ?>
                        <span class="faq-arrow">▾</span>
                    </button>
                    <div class="faq-a"><?php echo $t['faq_a2']; ?></div>
                </div>
                <div class="faq-item">
                    <button class="faq-q">
                        <?php echo $t['faq_q3']; ?>
                        <span class="faq-arrow">▾</span>
                    </button>
                    <div class="faq-a"><?php echo $t['faq_a3']; ?></div>
                </div>
                <div class="faq-item">
                    <button class="faq-q">
                        <?php echo $t['faq_q4']; ?>
                        <span class="faq-arrow">▾</span>
                    </button>
                    <div class="faq-a"><?php echo $t['faq_a4']; ?></div>
                </div>
                <div class="faq-item">
                    <button class="faq-q">
                        <?php echo $t['faq_q5']; ?>
                        <span class="faq-arrow">▾</span>
                    </button>
                    <div class="faq-a"><?php echo $t['faq_a5']; ?></div>
                </div>
            </div>
        </section>
        

        <section class="cta-section">
            <div class="cta-tag"><?php echo $t['cta_tag']; ?></div>
            <h2 class="cta-title"><?php echo $t['cta_title']; ?></h2>
            <p class="cta-sub"><?php echo $t['cta_sub']; ?></p>
            <div class="cta-btns">
                <button class="btn-cta-main" id="downloadBtn2">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="black" style="vertical-align:middle;margin-right:7px;"><path d="M0 3.449L9.75 2.1v9.451H0m10.949-9.602L24 0v11.4H10.949M0 12.6h9.75v9.451L0 20.699M10.949 12.6H24V24l-12.951-1.801"/></svg>
                    <?php echo $t['btn_download']; ?>
                </button>
                <button class="btn-cta-ghost"><?php echo $t['cta_btn_ghost']; ?></button>
            </div>
            <div class="cta-note"><span class="cta-dot"></span><?php echo $t['cta_note']; ?></div>
        </section>
        <!-- ПЛАВАЮЩИЙ НИЖНИЙ БАР -->
        <footer class="floating-bottom-bar">
            <div class="status-indicator-dot"></div>
            <span class="bottom-bar-text"><?php echo $t['bottom_bar']; ?></span>
        </footer>

    </div>

    <script src="script.js"></script>
</body>
</html>
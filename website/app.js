const slider = document.querySelector('#quotaSlider');
const moon = document.querySelector('#demoMoon');
const value = document.querySelector('#quotaValue');
const phase = document.querySelector('#phaseLabel');
const signal = document.querySelector('#signalText');

const imageForQuota = quota => {
  const bucket = Math.max(10, Math.ceil(quota / 10) * 10);
  return bucket === 100 ? 'assets/quota-wisp.png' : `assets/quota-wisp-${bucket}.png`;
};

const labelForQuota = quota => {
  if (quota > 90) return 'FULL MOON · 100%';
  const bucket = Math.max(10, Math.ceil(quota / 10) * 10);
  if (bucket === 50) return 'HALF MOON · 50%';
  return `${bucket > 50 ? 'WANING GIBBOUS' : 'WANING CRESCENT'} · ${bucket}%`;
};

const updateDemo = () => {
  const quota = Number(slider.value);
  value.textContent = quota;
  phase.textContent = labelForQuota(quota);
  moon.src = imageForQuota(quota);
  signal.textContent = quota < 20 ? 'SIGNAL CRITICAL' : quota < 40 ? 'SIGNAL LOW' : 'SIGNAL STRONG';
  signal.style.color = quota < 20 ? '#ff7188' : quota < 40 ? '#ffcc67' : '#77d8a1';
  slider.style.background = `linear-gradient(90deg,#53e9f7 ${quota}%,#193444 ${quota}%)`;
};

slider.addEventListener('input', updateDemo);
updateDemo();

const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });

document.querySelectorAll('.reveal').forEach(element => observer.observe(element));

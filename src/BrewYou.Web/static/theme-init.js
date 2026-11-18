(function () {
	try {
		var pref = localStorage.getItem('brewyou-theme');
		if (pref === 'chocolate-porter') {
			document.documentElement.classList.add('dark');
			document.documentElement.setAttribute('data-theme', 'chocolate-porter');
			document.documentElement.style.colorScheme = 'dark';
		} else if (pref === 'imperial-stout') {
			document.documentElement.classList.add('dark');
			document.documentElement.setAttribute('data-theme', 'imperial-stout');
			document.documentElement.style.colorScheme = 'dark';
		} else if (pref === 'obsidian') {
			document.documentElement.classList.add('dark');
			document.documentElement.setAttribute('data-theme', 'obsidian');
			document.documentElement.style.colorScheme = 'dark';
		} else if (pref === 'light') {
			document.documentElement.classList.remove('dark');
			document.documentElement.setAttribute('data-theme', 'light');
			document.documentElement.style.colorScheme = 'light';
		} else if (pref === 'dark') {
			document.documentElement.classList.add('dark');
			document.documentElement.setAttribute('data-theme', 'dark');
			document.documentElement.style.colorScheme = 'dark';
		} else {
			document.documentElement.classList.add('dark');
			document.documentElement.setAttribute('data-theme', 'imperial-stout');
			document.documentElement.style.colorScheme = 'dark';
		}
	} catch {
		// Ignore storage access errors during initial theme bootstrap
	}
})();

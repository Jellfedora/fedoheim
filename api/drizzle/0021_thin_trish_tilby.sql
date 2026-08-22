CREATE TABLE `config_files` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`modpack_id` integer NOT NULL,
	`filename` text NOT NULL,
	`download_url` text NOT NULL,
	`sha256` text NOT NULL,
	`updated_at` integer NOT NULL,
	FOREIGN KEY (`modpack_id`) REFERENCES `modpacks`(`id`) ON UPDATE no action ON DELETE no action
);

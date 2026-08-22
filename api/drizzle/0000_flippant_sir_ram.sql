CREATE TABLE `modpacks` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`slug` text NOT NULL,
	`name` text NOT NULL,
	`version` text NOT NULL,
	`updated_at` integer NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `modpacks_slug_unique` ON `modpacks` (`slug`);--> statement-breakpoint
CREATE TABLE `mods` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`modpack_id` integer NOT NULL,
	`name` text NOT NULL,
	`version` text NOT NULL,
	`install_path` text NOT NULL,
	`download_url` text NOT NULL,
	`sha256` text NOT NULL,
	FOREIGN KEY (`modpack_id`) REFERENCES `modpacks`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE TABLE `users` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`discord_id` text NOT NULL,
	`discord_username` text NOT NULL,
	`discord_avatar` text,
	`created_at` integer NOT NULL,
	`last_login_at` integer NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `users_discord_id_unique` ON `users` (`discord_id`);
CREATE TABLE `announcements` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`author` text NOT NULL,
	`message` text NOT NULL,
	`created_at` integer NOT NULL
);
